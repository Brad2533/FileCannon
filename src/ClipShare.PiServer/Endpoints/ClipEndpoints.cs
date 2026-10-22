using ClipShare.PiServer.Services;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace ClipShare.PiServer.Endpoints;

public static class ClipEndpoints
{
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    public static void MapClipEndpoints(this IEndpointRouteBuilder app)
    {
        // HEAD as well as GET: link-preview crawlers (Discord's included) typically probe
        // with HEAD before committing to fetching a large body, to check Content-Type/Length
        // first. MapGet alone only registers the GET verb, so HEAD 405'd and silently blocked
        // any embed preview from ever being generated. Results.File's executor already skips
        // writing the body for HEAD requests on its own, so no handler changes are needed.
        //
        // {extension?} is a trailing, optional, non-slash-separated segment — a "complex
        // segment" ASP.NET Core's router supports because {id:guid} carries a constraint that
        // bounds where it ends. The extension itself is never inspected server-side (identity
        // is resolved from {id} alone via the DB); it exists purely so the URL *looks* like
        // .../abc123.mp4 rather than a bare GUID, because link-embed crawlers (Discord's
        // included) commonly decide whether to attempt an inline media embed by pattern-
        // matching the URL's extension before ever making a request.
        app.MapMethods("/d/{id:guid}.{extension?}", [HttpMethods.Get, HttpMethods.Head], Download);
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
    }

    private static IResult Download(Guid id, string? extension, HttpContext context, ClipRepository repository, IOptions<PiServerOptions> options)
    {
        var clip = repository.GetById(id);
        if (clip is null || clip.ExpiresAtUtc <= DateTime.UtcNow)
        {
            return Results.NotFound();
        }

        var filePath = Path.Combine(options.Value.DataRoot, "clips", clip.StoredFileName);
        if (!File.Exists(filePath))
        {
            return Results.NotFound();
        }

        if (!ContentTypeProvider.TryGetContentType(clip.OriginalFileName, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        // "inline" rather than "attachment" — Results.File only forces a browser-download
        // disposition when a fileDownloadName is passed to it, which is what previously
        // blocked any native video player (in a browser, or in Discord's own link-embed
        // preview) from ever appearing on both mobile and desktop. Setting the header
        // ourselves keeps the original filename available without triggering that.
        context.Response.Headers.ContentDisposition =
            new ContentDispositionHeaderValue("inline") { FileNameStar = clip.OriginalFileName }.ToString();

        return Results.File(filePath, contentType, enableRangeProcessing: true);
    }
}
