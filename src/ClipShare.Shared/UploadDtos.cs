namespace ClipShare.Shared;

public sealed record UploadInitRequest(string FileName, long TotalSizeBytes, long ChunkSizeBytes);

public sealed record UploadInitResponse(Guid UploadId);

// DiscordChannelId is null when the overlay has no server selected (or none configured
// yet) — the Pi falls back to its own configured default channel in that case.
public sealed record UploadCompleteRequest(string? DiscordChannelId);

public sealed record UploadCompleteResponse(
    string DownloadUrl,
    DateTime ExpiresAtUtc,
    bool DiscordPosted,
    string? DiscordStatusMessage);
