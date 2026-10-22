using ClipShare.PiServer.Http;
using ClipShare.PiServer.Services;

namespace ClipShare.PiServer.Filters;

/// <summary>
/// Resolves the caller's <see cref="Account"/> from the <c>X-Api-Key</c> header and stashes it
/// on the request for downstream handlers. Replaces the old single-shared-key string compare.
///
/// Not doing a constant-time compare here on purpose: the lookup is keyed by a SHA-256 digest
/// of the presented key (see <see cref="ApiKeyHasher"/>), not the raw secret, so byte-wise
/// timing of the DB index probe leaks nothing an attacker could walk toward a valid key.
/// </summary>
public sealed class ApiKeyAuthFilter(AccountRepository accounts) : IEndpointFilter
{
    private const string HeaderName = "X-Api-Key";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var providedKey = context.HttpContext.Request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(providedKey))
        {
            return Results.Unauthorized();
        }

        var account = accounts.GetByApiKeyHash(ApiKeyHasher.Hash(providedKey));
        if (account is null)
        {
            return Results.Unauthorized();
        }

        context.HttpContext.SetAccount(account);
        return await next(context);
    }
}

/// <summary>
/// Chained after <see cref="ApiKeyAuthFilter"/> on the <c>/api/admin</c> group: rejects any
/// authenticated caller whose account isn't the bootstrap/owner key.
/// </summary>
public sealed class AdminOnlyFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!context.HttpContext.GetAccount().IsAdmin)
        {
            return Results.Problem("This endpoint requires the admin API key.", statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context);
    }
}
