namespace ClipShare.Shared;

/// <summary>Admin-only. Provisions a new account and returns its freshly generated key once.</summary>
/// <param name="Tier">"Free" or "Pro".</param>
/// <param name="Label">Optional human note stored with the account.</param>
public sealed record CreateAccountRequest(string Tier, string? Label);

/// <summary>
/// The <c>apiKey</c> here is the only time the raw key is ever returned — the server stores
/// only its hash. Callers must capture it now or provision a replacement.
/// </summary>
public sealed record CreateAccountResponse(Guid AccountId, string ApiKey, string Tier, string? Label);

/// <summary>One row in the admin account listing. No key material — hashes are never returned.</summary>
public sealed record AccountSummary(
    Guid AccountId,
    string Tier,
    bool IsAdmin,
    string? Label,
    bool HasStripeCustomer,
    DateTime CreatedAtUtc);

/// <summary>What <c>GET /api/account</c> tells a caller about its own key. Lets a client show tier without guessing.</summary>
public sealed record AccountInfoResponse(
    Guid AccountId,
    string Tier,
    bool IsAdmin,
    string? Label,
    DateTime CreatedAtUtc);
