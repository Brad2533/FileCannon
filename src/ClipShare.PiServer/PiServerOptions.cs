namespace ClipShare.PiServer;

public sealed class PiServerOptions
{
    public required string DataRoot { get; set; }

    /// <summary>
    /// The bootstrap / owner key. On startup, if this is set and no <c>Accounts</c> row matches
    /// its hash, an admin account (Pro tier, <c>IsAdmin = true</c>) is seeded for it — so the
    /// existing single-key overlay setup keeps working unchanged, and this key is what calls
    /// <c>/api/admin/*</c> to provision every other account. Rotating it just seeds a new admin
    /// account on next start; the old row stays until deleted.
    /// </summary>
    public required string ApiKey { get; set; }

    /// <summary>Fallback retention when a tier doesn't override it. Per-tier retention is Phase 2.</summary>
    public double RetentionHours { get; set; } = 48;

    public string DiscordBotToken { get; set; } = "";
    public string DiscordChannelId { get; set; } = "";
}
