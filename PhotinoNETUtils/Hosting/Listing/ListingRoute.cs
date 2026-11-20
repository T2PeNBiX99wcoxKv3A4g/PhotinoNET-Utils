using System.Globalization;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Serilog;

namespace PhotinoNetUtils.Hosting;

/// <summary>
///     Serves one page of a folder listing as the binary block in <see cref="ListingFormat" />, and the
///     same page as text for whoever is writing a client. Paging, sorting and the folder's signature
///     happen here, so the window asks for what it is about to draw instead of receiving the folder.
/// </summary>
/// <remarks>
///     The route lives under <c>/data/&lt;token&gt;/</c>, which is the prefix <see cref="StaticServer" />
///     hands to its route map. Requests carry:
///     <c>path</c>, <c>from</c>, <c>count</c>, <c>sort</c> (name, size, modified, created, type),
///     <c>dir=desc</c>, and <c>format=text</c> for the readable form.
/// </remarks>
[PublicAPI]
public static class ListingRoute
{
    private const int DefaultCount = 400;
    private const int MaxCount = 100_000;
    private const int CacheFolders = 6;

    /// <summary>Registers <c>/data/&lt;token&gt;/list</c> on the app's local server.</summary>
    public static void Map(WebApplication app, string token, IListingSource source)
    {
        var cache = new ListingCache(source, CacheFolders);

        app.MapGet($"/data/{token}/list", (Delegate)((HttpContext context) => ListAsync(context, cache)));
    }

    private static async Task<IResult> ListAsync(HttpContext context, ListingCache cache)
    {
        var query = context.Request.Query;
        var path = query["path"].ToString();

        if (path.Length == 0)
            return Results.BadRequest();

        var from = Math.Max(0, ReadNumber(query["from"], 0));
        var count = Math.Clamp(ReadNumber(query["count"], DefaultCount), 0, MaxCount);
        var sort = query["sort"].ToString() is { Length: > 0 } value ? value : "name";
        var descending = string.Equals(query["dir"], "desc", StringComparison.OrdinalIgnoreCase);

        try
        {
            var page = await cache.ReadAsync(path, sort, descending, from, count, context.RequestAborted);

            var etag = $"\"{page.Signature:x8}\"";

            // The signature is the folder's content, so a repeat request for the first page can be
            // answered without a body at all.
            context.Response.Headers.ETag = etag;
            context.Response.Headers["X-Yafm-Total"] = page.Total.ToString(CultureInfo.InvariantCulture);
            context.Response.Headers.CacheControl = "no-cache";

            if (from == 0 && context.Request.Headers.IfNoneMatch == etag)
                return Results.StatusCode(StatusCodes.Status304NotModified);

            var block = ListingFormat.Encode(page.Entries, page.Path);

            return string.Equals(query["format"], "text", StringComparison.OrdinalIgnoreCase)
                ? Results.Text(ListingFormat.Dump(block, page.Total), "text/plain; charset=utf-8")
                : Results.Bytes(block, ListingFormat.ContentType);
        }
        catch (OperationCanceledException)
        {
            return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not list {Path} for the data plane", path);
            return Results.NotFound();
        }
    }

    private static int ReadNumber(string? value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
}
