namespace ClipShare.PiServer.Services;

/// <summary>
/// A single API-key holder. Replaces the old server-wide shared key: every overlay /
/// PWA install now authenticates as one of these rows instead of all sharing one secret,
/// which is the prerequisite for per-account size/retention tiers and billing later.
/// </summary>
/// <param name="ApiKeyHash">Lowercase hex SHA-256 of the raw key. The raw key is never stored.</param>
/// <param name="Tier">One of <see cref="AccountTiers"/>. Free until a paid upgrade flips it.</param>
/// <param name="IsAdmin">Bootstrap/owner key — the only kind allowed to call <c>/api/admin/*</c>.</param>
/// <param name="StripeCustomerId">Null until Phase 4 billing links a Stripe customer.</param>
/// <param name="Label">Human note for the owner ("brad-desktop", "phone-pwa"); optional.</param>
public sealed record Account(
    Guid Id,
    string ApiKeyHash,
    string Tier,
    bool IsAdmin,
    string? StripeCustomerId,
    string? Label,
    DateTime CreatedAtUtc);

/// <summary>
/// Tier names, stored verbatim in the <c>Accounts.Tier</c> column. Kept as plain strings
/// rather than an enum so the DB stays readable and adding a tier later is a one-line change
/// with no migration.
/// </summary>
public static class AccountTiers
{
    public const string Free = "Free";
    public const string Pro = "Pro";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Free, Pro
    };

    public static bool IsValid(string? tier) => tier is not null && All.Contains(tier);
}
