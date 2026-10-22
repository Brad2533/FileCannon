namespace ClipShare.Overlay.Services;

public sealed class DiscordServerEntry
{
    public string Name { get; set; } = "";
    public string ChannelId { get; set; } = "";
}

public sealed class ClipShareSettings
{
    public string PiUploadUrl { get; set; } = "";
    public string PiApiKey { get; set; } = "";
    public long ChunkSizeBytes { get; set; } = 80 * 1024 * 1024;

    // Channel IDs aren't secrets (the bot token that actually authenticates stays
    // server-side on the Pi) — safe to keep this small list local to the overlay so the
    // upload popup can offer a per-upload "which server?" dropdown without a round trip.
    public List<DiscordServerEntry> DiscordServers { get; set; } = [];
    public string? LastSelectedServerName { get; set; }
}
