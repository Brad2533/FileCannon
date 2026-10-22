using ClipShare.PiServer.Filters;
using ClipShare.PiServer.Http;
using ClipShare.PiServer.Services;
using ClipShare.Shared;

namespace ClipShare.PiServer.Endpoints;

/// <summary>
/// Account provisioning, gated to the bootstrap/owner key (<see cref="AdminOnlyFilter"/>).
/// This is the only way to mint a per-user key until Phase 4 self-serve signup / billing
/// exists.
/// </summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin")
            .AddEndpointFilter<ApiKeyAuthFilter>()
            .AddEndpointFilter<AdminOnlyFilter>();

        group.MapPost("/accounts", CreateAccount);
        group.MapGet("/accounts", ListAccounts);
        group.MapDelete("/accounts/{id:guid}", DeleteAccount);
    }

    private static IResult CreateAccount(CreateAccountRequest request, AccountRepository accounts, ILoggerFactory loggerFactory)
    {
        if (!AccountTiers.IsValid(request.Tier))
        {
            return Results.Problem(
                $"Unknown tier '{request.Tier}'. Valid tiers: {string.Join(", ", AccountTiers.All)}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var rawKey = ApiKeyHasher.GenerateRawKey();
        var account = new Account(
            Id: Guid.NewGuid(),
            ApiKeyHash: ApiKeyHasher.Hash(rawKey),
            Tier: request.Tier,
            IsAdmin: false,
            StripeCustomerId: null,
            Label: string.IsNullOrWhiteSpace(request.Label) ? null : request.Label.Trim(),
            CreatedAtUtc: DateTime.UtcNow);

        accounts.Insert(account);
        loggerFactory.CreateLogger(typeof(AdminEndpoints)).LogInformation(
            "Provisioned account {AccountId} tier {Tier} label {Label}", account.Id, account.Tier, account.Label ?? "(none)");

        // 201 with the raw key in the body — the one and only time it leaves the server.
        return Results.Created(
            $"/api/admin/accounts/{account.Id}",
            new CreateAccountResponse(account.Id, rawKey, account.Tier, account.Label));
    }

    /// <summary>Every account, mapped to <see cref="AccountSummary"/> so the API key hash never leaves the server.</summary>
    private static IResult ListAccounts(AccountRepository accounts) =>
        Results.Ok(accounts.GetAll().Select(a => new AccountSummary(
            a.Id, a.Tier, a.IsAdmin, a.Label, a.StripeCustomerId is not null, a.CreatedAtUtc)));

    private static IResult DeleteAccount(Guid id, HttpContext http, AccountRepository accounts)
    {
        var caller = http.GetAccount();
        if (caller.Id == id)
        {
            // Deleting the key you're authenticating with locks everyone out of /api/admin
            // (the bootstrap account only re-seeds when its configured key has no row).
            return Results.Problem("Refusing to delete the account making this request.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return accounts.Delete(id) ? Results.NoContent() : Results.NotFound();
    }
}
