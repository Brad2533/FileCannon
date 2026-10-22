using ClipShare.PiServer;
using ClipShare.PiServer.Endpoints;
using ClipShare.PiServer.Filters;
using ClipShare.PiServer.Services;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Default ASP.NET Core max request body size (~28.6MB) is below even a single ~80MB
// chunk, so it must be raised explicitly or uploads fail with a confusing mid-stream
// error. Chunks are sent as raw octet-stream rather than multipart, which sidesteps
// the separate MultipartBodyLengthLimit default.
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 100L * 1024 * 1024);

builder.Services.Configure<PiServerOptions>(builder.Configuration.GetSection("PiServer"));
builder.Services.AddSingleton<PendingUploadStore>();
builder.Services.AddSingleton<ClipRepository>();
builder.Services.AddSingleton<AccountRepository>();
builder.Services.AddSingleton<DiscordNotifier>();
builder.Services.AddTransient<ApiKeyAuthFilter>();
builder.Services.AddTransient<AdminOnlyFilter>();
builder.Services.AddHostedService<ClipCleanupService>();

var app = builder.Build();

var options = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<PiServerOptions>>().Value;
// Results.File treats a relative path as a *virtual* path resolved against the content
// root's file provider rather than a physical one, which fails outright for any DataRoot
// outside that tree (e.g. /mnt/clipshare-hdd on the Pi). Resolving to an absolute path
// once here keeps every downstream consumer (repository, endpoints, cleanup) consistent
// and makes physical-file resolution unambiguous.
options.DataRoot = Path.GetFullPath(options.DataRoot);
Directory.CreateDirectory(options.DataRoot);
Directory.CreateDirectory(Path.Combine(options.DataRoot, "clips"));
Directory.CreateDirectory(Path.Combine(options.DataRoot, "tmp"));
app.Services.GetRequiredService<ClipRepository>().Initialize();

var accounts = app.Services.GetRequiredService<AccountRepository>();
accounts.Initialize();
SeedBootstrapAdminAccount(accounts, options.ApiKey, app.Services.GetRequiredService<ILoggerFactory>());

// cloudflared terminates TLS at Cloudflare's edge and forwards plain HTTP to this loopback
// Kestrel instance, so request.Scheme would otherwise come out "http" even for public HTTPS
// requests — which would bake a wrong-scheme link into every downloadUrl. Kestrel only
// accepts connections from cloudflared (loopback-bound), so trusting forwarded headers here
// is safe: there's no other path in.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
});

app.MapUploadEndpoints();
app.MapClipEndpoints();
app.MapAccountEndpoints();
app.MapAdminEndpoints();

app.Run();

// Keeps the pre-multi-account setup working with zero client changes: the key that used to be
// the whole server's shared secret becomes account #1 (Pro, admin) the first time the server
// starts after this change. Re-seeds only when the configured key has no matching row, so
// rotating PiServer:ApiKey mints a fresh admin account and a blank key is a no-op (dev uses
// user-secrets; a fresh server with no key and no accounts simply has nobody who can call
// /api/admin until a key is configured).
static void SeedBootstrapAdminAccount(AccountRepository accounts, string? configuredKey, ILoggerFactory loggerFactory)
{
    var logger = loggerFactory.CreateLogger("Bootstrap");

    if (string.IsNullOrWhiteSpace(configuredKey))
    {
        if (accounts.Count() == 0)
        {
            logger.LogWarning(
                "PiServer:ApiKey is not set and no accounts exist — no key can authenticate. " +
                "Set PiServer:ApiKey (or dotnet user-secrets) to seed the bootstrap admin account.");
        }
        return;
    }

    var hash = ApiKeyHasher.Hash(configuredKey);
    if (accounts.GetByApiKeyHash(hash) is not null)
    {
        return;
    }

    accounts.Insert(new Account(
        Id: Guid.NewGuid(),
        ApiKeyHash: hash,
        Tier: AccountTiers.Pro,
        IsAdmin: true,
        StripeCustomerId: null,
        Label: "bootstrap-admin",
        CreatedAtUtc: DateTime.UtcNow));

    logger.LogInformation("Seeded bootstrap admin account from PiServer:ApiKey.");
}
