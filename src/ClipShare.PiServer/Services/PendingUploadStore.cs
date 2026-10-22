using System.Collections.Concurrent;

namespace ClipShare.PiServer.Services;

public sealed class PendingUploadStore
{
    private readonly ConcurrentDictionary<Guid, PendingUpload> _uploads = new();

    public void Add(Guid uploadId, PendingUpload upload) => _uploads[uploadId] = upload;

    public bool TryGet(Guid uploadId, out PendingUpload upload) => _uploads.TryGetValue(uploadId, out upload!);

    public bool TryRemove(Guid uploadId, out PendingUpload upload) => _uploads.TryRemove(uploadId, out upload!);

    public IReadOnlyCollection<KeyValuePair<Guid, PendingUpload>> Snapshot() => _uploads.ToArray();
}
