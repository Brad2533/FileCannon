using ClipShare.PiServer.Filters;
using ClipShare.PiServer.Http;
using ClipShare.Shared;

namespace ClipShare.PiServer.Endpoints;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/account").AddEndpointFilter<ApiKeyAuthFilter>();

        // "Who am I" — a client calls this once after configuring its key to confirm the key
        // works and learn its tier, instead of inferring tier from a failed upload later.
        group.MapGet("/", (HttpContext http) =>
        {
            var account = http.GetAccount();
            return Results.Ok(new AccountInfoResponse(
                account.Id, account.Tier, account.IsAdmin, account.Label, account.CreatedAtUtc));
        });
    }
}
