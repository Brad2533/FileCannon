using ClipShare.PiServer.Filters;
using ClipShare.PiServer.Services;
using ClipShare.Shared;
using Microsoft.Extensions.Options;

namespace ClipShare.PiServer.Endpoints;

public static class UploadEndpoints
{
    public static void MapUploadEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/upload").AddEndpointFilter<ApiKeyAuthFilter>();

        group.MapPost("/init", Init);
        group.MapPost("/{uploadId:guid}/chunk", Chunk);
        group.MapPost("/{uploadId:guid}/complete", Complete);
    }

    private static IResult Init(
        UploadInitRequest request,
        PendingUploadStore uploads,
        IOptions<PiServerOptions> options)
    {
        var uploadId = Guid.NewGuid();
        var tempPath = Path.Combine(options.Value.DataRoot, "tmp", $"{uploadId}.part");
        Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);

        // Reserve the file up front so a chunk arriving before this returns still has somewhere to append to.
        using (File.Create(tempPath)) { }

        uploads.Add(uploadId, new PendingUpload
        {
            FileName = request.FileName,
            TotalSizeBytes = request.TotalSizeBytes,
            ChunkSizeBytes = request.ChunkSizeBytes,
            TempFilePath = tempPath,
            BytesReceived = 0,
            LastActivityUtc = DateTime.UtcNow
        });

        return Results.Ok(new UploadInitResponse(uploadId));
    }

    private static async Task<IResult> Chunk(Guid uploadId, HttpRequest request, PendingUploadStore uploads)
    {
        if (!uploads.TryGet(uploadId, out var upload))
        {
            return Results.NotFound($"No pending upload with id {uploadId}.");
        }

        // Chunks are expected to arrive in order, so a plain sequential append is the
        // simplest correct reassembly — no offset/range header bookkeeping needed.
        await using (var fileStream = new FileStream(upload.TempFilePath, FileMode.Append, FileAccess.Write, FileShare.None))
        {
            await request.Body.CopyToAsync(fileStream);
        }

        upload.BytesReceived = new FileInfo(upload.TempFilePath).Length;
        upload.LastActivityUtc = DateTime.UtcNow;

        return Results.Ok();
    }

    private static async Task<IResult> Complete(
        Guid uploadId,
        HttpRequest request,
        UploadCompleteRequest? completeRequest,
        PendingUploadStore uploads,
        ClipRepository repository,
        DiscordNotifier discordNotifier,
        IOptions<PiServerOptions> options)
    {
        if (!uploads.TryRemove(uploadId, out var upload))
        {
            return Results.NotFound($"No pending upload with id {uploadId}.");
        }

        var actualSize = new FileInfo(upload.TempFilePath).Length;
        if (actualSize != upload.TotalSizeBytes)
        {
            return Results.BadRequest(
                $"Received {actualSize} bytes but expected {upload.TotalSizeBytes} — upload incomplete or corrupted.");
        }

        var extension = Path.GetExtension(upload.FileName);
        var clipId = Guid.NewGuid();
        var storedFileName = $"{clipId}{extension}";
        var finalPath = Path.Combine(options.Value.DataRoot, "clips", storedFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        File.Move(upload.TempFilePath, finalPath);

        var now = DateTime.UtcNow;
        var expiresAtUtc = now.AddHours(options.Value.RetentionHours);

        repository.Insert(new Clip(clipId, upload.FileName, storedFileName, actualSize, now, expiresAtUtc));

        // Extension appended even though the /d/{id} route resolves purely by GUID — it's
        // there so the URL looks like .../abc123.mp4 rather than a bare GUID, which is what
        // link-embed crawlers (Discord's included) commonly key off before attempting an
        // inline media embed.
        var downloadUrl = $"{request.Scheme}://{request.Host}/d/{clipId}{extension}";

        // The clip is already stored and the link already built above, so a Discord failure
        // (or no bot configured) never loses the link — it just gets reported back to the
        // caller alongside a 200, the same link-first guarantee the overlay used to enforce
        // itself before this moved server-side.
        // Overlay sends the channel the user picked from its server dropdown; falls back to
        // the single configured default channel when nothing was selected (old client, or no
        // servers added yet), keeping the pre-multi-server behavior as the safety net.
        var targetChannelId = string.IsNullOrWhiteSpace(completeRequest?.DiscordChannelId)
            ? options.Value.DiscordChannelId
            : completeRequest.DiscordChannelId;

        var (discordPosted, discordStatusMessage) = await TryPostToDiscordAsync(discordNotifier, options.Value, targetChannelId, downloadUrl);

        return Results.Ok(new UploadCompleteResponse(downloadUrl, expiresAtUtc, discordPosted, discordStatusMessage));
    }

    private static async Task<(bool Posted, string? StatusMessage)> TryPostToDiscordAsync(
        DiscordNotifier discordNotifier, PiServerOptions options, string? targetChannelId, string downloadUrl)
    {
        if (string.IsNullOrWhiteSpace(options.DiscordBotToken) || string.IsNullOrWhiteSpace(targetChannelId)
            || !ulong.TryParse(targetChannelId, out var channelId))
        {
            return (false, "Discord post skipped: bot token/channel not configured.");
        }

        try
        {
            await discordNotifier.PostLinkAsync(options.DiscordBotToken, channelId, downloadUrl);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Discord post failed: {DescribeDiscordFailure(ex)}");
        }
    }

    /// <summary>
    /// Discord's raw errors for the two failures the multi-server dropdown actually hits
    /// ("Missing Access"/"Missing Permissions") say nothing about the cause, which is
    /// almost always that the bot was never invited to the server that channel lives in —
    /// adding a server in the overlay only records its channel ID locally, it can't invite
    /// the bot. Translate those two into the actual next step.
    /// </summary>
    private static string DescribeDiscordFailure(Exception ex)
    {
        var message = ex.Message;

        if (message.Contains("50001") || message.Contains("Missing Access", StringComparison.OrdinalIgnoreCase))
        {
            return "the bot isn't in that server (or can't see that channel). Invite it to the server, then retry. " + message;
        }

        if (message.Contains("50013") || message.Contains("Missing Permissions", StringComparison.OrdinalIgnoreCase))
        {
            return "the bot is in that server but lacks Send Messages/Embed Links on that channel. " + message;
        }

        return message;
    }
}
