using ClipShare.PiServer.Services;

namespace ClipShare.PiServer.Http;

/// <summary>
/// Bridges the account resolved by <c>ApiKeyAuthFilter</c> to downstream endpoint handlers.
/// The filter stashes the row on <see cref="HttpContext.Items"/>; handlers behind the filter
/// read it back with <see cref="GetAccount"/> and can rely on it being non-null.
/// </summary>
public static class HttpContextAccountExtensions
{
    public const string AccountItemKey = "ClipShare.Account";

    /// <summary>Stashes the account <c>ApiKeyAuthFilter</c> resolved so downstream handlers can read it back with <see cref="GetAccount"/>.</summary>
    public static void SetAccount(this HttpContext context, Account account) =>
        context.Items[AccountItemKey] = account;

    /// <summary>The authenticated account. Non-null in any handler that runs behind <c>ApiKeyAuthFilter</c>.</summary>
    public static Account GetAccount(this HttpContext context) =>
        context.Items[AccountItemKey] as Account
        ?? throw new InvalidOperationException(
            "No authenticated account on the request — GetAccount() was called outside an ApiKeyAuthFilter-protected endpoint.");

    /// <summary>Same as <see cref="GetAccount"/>, but returns <c>null</c> instead of throwing when no account was resolved.</summary>
    public static Account? GetAccountOrNull(this HttpContext context) =>
        context.Items[AccountItemKey] as Account;
}
