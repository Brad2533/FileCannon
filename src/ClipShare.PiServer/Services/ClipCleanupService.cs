using Microsoft.Extensions.Options;

namespace ClipShare.PiServer.Services;

/// <summary>
/// Deletes expired clips and sweeps abandoned chunked-upload temp files. Expiry is read
/// from SQLite rather than tracked in-memory, so a restart resumes correctly with zero
/// lost state — ticks just pick up wherever the table says things are.
/// </summary>
public sealed class ClipCleanupService(ClipRepository repository, IOptions<PiServerOptions> options, ILogger<ClipCleanupService> logger)
    : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan OrphanTempFileAge = TimeSpan.FromHours(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        RunCleanupPass();

        using var timer = new PeriodicTimer(TickInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            RunCleanupPass();
        }
    }

    private void RunCleanupPass()
    {
        DeleteExpiredClips();
        SweepOrphanedTempFiles();
    }

    private void DeleteExpiredClips()
    {
        var now = DateTime.UtcNow;
        foreach (var clip in repository.GetExpired(now))
        {
            var filePath = Path.Combine(options.Value.DataRoot, "clips", clip.StoredFileName);
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                repository.Delete(clip.Id);
                logger.LogInformation("Deleted expired clip {ClipId} ({FileName})", clip.Id, clip.OriginalFileName);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Failed to delete expired clip {ClipId}, will retry next pass", clip.Id);
            }
        }
    }

    private void SweepOrphanedTempFiles()
    {
        var tmpDir = Path.Combine(options.Value.DataRoot, "tmp");
        if (!Directory.Exists(tmpDir))
        {
            return;
        }

        var cutoff = DateTime.UtcNow - OrphanTempFileAge;
        foreach (var partFile in Directory.EnumerateFiles(tmpDir, "*.part"))
        {
            if (File.GetLastWriteTimeUtc(partFile) >= cutoff)
            {
                continue;
            }

            try
            {
                File.Delete(partFile);
                logger.LogInformation("Swept orphaned upload temp file {File}", partFile);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Failed to delete orphaned temp file {File}, will retry next pass", partFile);
            }
        }
    }
}
