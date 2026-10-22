using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClipShare.Shared;

namespace ClipShare.Overlay.Services;

public sealed class PiUploadException(string message) : Exception(message);

public sealed class PiUploadClient
{
    private const int MaxAttemptsPerChunk = 3;

    // A dedicated long-timeout client: the default 100s HttpClient timeout would otherwise
    // abort large chunk uploads on a typical home connection. Kept as a single static
    // instance for the app's lifetime to avoid socket exhaustion from creating one per upload.
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(30) };

    public async Task<UploadCompleteResponse> UploadAsync(
        string filePath,
        string piUploadUrl,
        string apiKey,
        long chunkSizeBytes,
        string? discordChannelId,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var baseUrl = piUploadUrl.TrimEnd('/');
        var fileInfo = new FileInfo(filePath);

        var uploadId = await InitAsync(baseUrl, apiKey, fileInfo.Name, fileInfo.Length, chunkSizeBytes, cancellationToken);

        await using (var fileStream = File.OpenRead(filePath))
        {
            var buffer = new byte[chunkSizeBytes];
            long bytesSent = 0;

            while (bytesSent < fileInfo.Length)
            {
                var bytesRead = await ReadFullyAsync(fileStream, buffer, cancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }

                await SendChunkWithRetryAsync(baseUrl, apiKey, uploadId, buffer, bytesRead, cancellationToken);

                bytesSent += bytesRead;
                progress?.Report((double)bytesSent / fileInfo.Length);
            }
        }

        return await CompleteAsync(baseUrl, apiKey, uploadId, discordChannelId, cancellationToken);
    }

    private static async Task<Guid> InitAsync(
        string baseUrl, string apiKey, string fileName, long totalSizeBytes, long chunkSizeBytes, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/upload/init")
        {
            Content = JsonContent.Create(new UploadInitRequest(fileName, totalSizeBytes, chunkSizeBytes))
        };
        request.Headers.Add("X-Api-Key", apiKey);

        using var response = await SendAsync(request, ct);
        var body = await response.Content.ReadFromJsonAsync<UploadInitResponse>(cancellationToken: ct)
            ?? throw new PiUploadException("Pi server returned an empty response to upload init.");
        return body.UploadId;
    }

    private static async Task SendChunkWithRetryAsync(
        string baseUrl, string apiKey, Guid uploadId, byte[] buffer, int length, CancellationToken ct)
    {
        Exception? lastError = null;

        for (var attempt = 1; attempt <= MaxAttemptsPerChunk; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/upload/{uploadId}/chunk");
                request.Headers.Add("X-Api-Key", apiKey);
                request.Content = new ByteArrayContent(buffer, 0, length)
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") }
                };

                using var response = await SendAsync(request, ct);
                return;
            }
            catch (Exception ex) when (ex is not PiUploadException && attempt < MaxAttemptsPerChunk)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromSeconds(attempt), ct);
            }
        }

        throw lastError is not null
            ? new PiUploadException($"Could not reach upload server after {MaxAttemptsPerChunk} attempts: {lastError.Message}")
            : new PiUploadException("Chunk upload failed for an unknown reason.");
    }

    private static async Task<UploadCompleteResponse> CompleteAsync(
        string baseUrl, string apiKey, Guid uploadId, string? discordChannelId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/upload/{uploadId}/complete")
        {
            Content = JsonContent.Create(new UploadCompleteRequest(discordChannelId))
        };
        request.Headers.Add("X-Api-Key", apiKey);

        using var response = await SendAsync(request, ct);
        return await response.Content.ReadFromJsonAsync<UploadCompleteResponse>(cancellationToken: ct)
            ?? throw new PiUploadException("Pi server returned an empty response to upload complete.");
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await Client.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PiUploadException($"Could not reach upload server: {ex.Message}");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new PiUploadException("Upload rejected — check API key in Settings.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new PiUploadException(string.IsNullOrWhiteSpace(body)
                ? $"Pi server returned {(int)response.StatusCode} {response.ReasonPhrase}."
                : body);
        }

        return response;
    }

    private static async Task<int> ReadFullyAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct);
            if (read == 0)
            {
                break;
            }
            totalRead += read;
        }
        return totalRead;
    }
}
