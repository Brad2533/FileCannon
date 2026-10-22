namespace ClipShare.PiServer.Services;

public sealed record Clip(
    Guid Id,
    string OriginalFileName,
    string StoredFileName,
    long SizeBytes,
    DateTime UploadedAtUtc,
    DateTime ExpiresAtUtc);
