namespace ClipShare.PiServer.Services;

/// <summary>
/// Tracks an in-progress chunked upload. Deliberately not persisted — if the service
/// restarts mid-transfer the client just retries the whole upload from scratch, which is
/// an acceptable tradeoff at personal-tool scale.
/// </summary>
public sealed class PendingUpload
{
    public required string FileName { get; init; }
    public required long TotalSizeBytes { get; init; }
    public required long ChunkSizeBytes { get; init; }
    public required string TempFilePath { get; init; }
    public long BytesReceived { get; set; }
    public DateTime LastActivityUtc { get; set; }
}
