using System.Security.Cryptography;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;

namespace PhotinoNetUtils.Hosting;

/// <summary>
///     Binary responses with the headers a browser cache understands. Images, thumbnails and other
///     bytes travel as a response instead of a base64 string through the message bridge.
/// </summary>
[PublicAPI]
public static class BinaryContent
{
    /// <summary>A byte array with an optional ETag and cache lifetime.</summary>
    public static IResult Bytes(byte[] bytes, string contentType, string? etag = null,
        TimeSpan? maxAge = null, IReadOnlyDictionary<string, string>? headers = null) =>
        new Configured(Results.Bytes(bytes, contentType), context =>
        {
            Apply(context, etag, maxAge, headers);
        });

    /// <summary>
    ///     A file, with range requests enabled by default so a large image or video can be fetched in
    ///     pieces. The caller still owns the file's lifetime; the result only reads it.
    /// </summary>
    public static IResult File(string path, string contentType, DateTimeOffset? lastModified = null,
        string? etag = null, TimeSpan? maxAge = null, bool range = true,
        IReadOnlyDictionary<string, string>? headers = null) =>
        new Configured(Results.File(path, contentType, enableRangeProcessing: range,
            lastModified: lastModified), context =>
        {
            Apply(context, etag, maxAge, headers);
        });

    /// <summary>A quoted ETag derived from the bytes; stable until the bytes change.</summary>
    public static string ETag(ReadOnlySpan<byte> bytes) =>
        $"\"{Convert.ToHexString(SHA256.HashData(bytes)[..8]).ToLowerInvariant()}\"";

    private static void Apply(HttpContext context, string? etag, TimeSpan? maxAge,
        IReadOnlyDictionary<string, string>? headers)
    {
        if (etag is { Length: > 0 })
            context.Response.Headers.ETag = etag;

        context.Response.Headers.CacheControl = maxAge is { } lifetime
            ? $"public, max-age={Math.Max(0, (long)lifetime.TotalSeconds)}"
            : "no-cache";

        if (headers is null)
            return;

        foreach (var (name, value) in headers)
            context.Response.Headers[name] = value;
    }

    private sealed class Configured(IResult inner, Action<HttpContext> configure) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            configure(httpContext);
            return inner.ExecuteAsync(httpContext);
        }
    }
}
