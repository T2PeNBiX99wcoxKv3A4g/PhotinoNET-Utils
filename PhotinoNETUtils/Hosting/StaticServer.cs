using System.Reflection;
using System.Security.Cryptography;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace PhotinoNetUtils.Hosting;

/// <summary>
///     The local Kestrel instance that serves the built interface to a Photino web view.
///     It binds to port 0, so the OS hands out a port that is really free instead of every Photino
///     app racing for 8000 — which is what <c>Photino.NET.Server</c> does, with a scan that can miss
///     an occupied port and then fail to bind.
/// </summary>
[PublicAPI]
public sealed class StaticServer : IDisposable
{
    private readonly WebApplication _application;

    private StaticServer(WebApplication application, Uri address, string token)
    {
        _application = application;
        Address = address;
        Token = token;
    }

    /// <summary>The address Kestrel actually bound, e.g. <c>http://127.0.0.1:54321</c>.</summary>
    public Uri Address { get; }

    /// <summary>The address without a trailing slash, ready for composing page URLs.</summary>
    public string BaseUrl => Address.GetLeftPart(UriPartial.Authority);

    /// <summary>
    ///     This run's secret. Routes registered by <c>map</c> live under <c>/data/&lt;token&gt;/</c>, so
    ///     another process on the machine cannot reach them by guessing a path.
    /// </summary>
    public string Token { get; }

    /// <summary>The prefix for everything an app registers through <c>map</c>.</summary>
    public string DataUrl => $"{BaseUrl}/data/{Token}";

    public void Dispose() => _application.DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>
    ///     Starts the server and waits until Kestrel reports a real port. The content root is a
    ///     folder owned by the app, so the server cannot drop a <c>wwwroot</c> next to the executable.
    ///     Embedded files are read from <paramref name="assembly" /> under
    ///     <paramref name="embeddedResourcePath" />.
    /// </summary>
    /// <param name="contentRoot">Folder Kestrel may write to.</param>
    /// <param name="assembly">Assembly whose embedded files are served when no file is on disk.</param>
    /// <param name="embeddedResourcePath">Prefix of those embedded files.</param>
    /// <param name="map">
    ///     Registers the app's routes; the second parameter is this run's <see cref="Token" />. The
    ///     routes answer under <c>/data/&lt;token&gt;/…</c> by convention and rarely need CORS.
    /// </param>
    /// <param name="allowedOrigins">
    ///     Extra origins allowed to call the routes, such as the Vite dev server in a debug build.
    ///     Static files and subresources are unaffected.
    /// </param>
    public static StaticServer Start(
        string contentRoot,
        Assembly? assembly = null,
        string embeddedResourcePath = "Resources/wwwroot",
        Action<WebApplication, string>? map = null,
        IEnumerable<string>? allowedOrigins = null)
    {
        var webRoot = Path.Combine(contentRoot, "wwwroot");
        Directory.CreateDirectory(webRoot);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = contentRoot,
            WebRootPath = webRoot
        });

        // Serilog owns the console; Kestrel's own startup chatter would only duplicate it.
        builder.Logging.ClearProviders();

        // Port 0 asks the OS for a free port. The real one is read back after StartAsync below.
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var origins = allowedOrigins?.ToArray() ?? [];

        if (origins.Length > 0)
            builder.Services.AddCors();

        // Files on disk win, so a build can be overridden without touching the embedded copy.
        builder.Environment.WebRootFileProvider = new CompositeFileProvider(
            builder.Environment.WebRootFileProvider,
            new ManifestEmbeddedFileProvider(
                assembly ?? Assembly.GetEntryAssembly() ?? Assembly.GetCallingAssembly(),
                embeddedResourcePath));

        var application = builder.Build();

        if (origins.Length > 0)
            application.UseCors(policy => policy
                .WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod());

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        application.UseDefaultFiles();
        application.UseStaticFiles(new StaticFileOptions
        {
            DefaultContentType = "text/plain"
        });

        // After the static files, so a route and a file never fight over a name: the route wins where
        // it exists and everything else still falls through to the built interface.
        map?.Invoke(application, token);

        application.StartAsync().GetAwaiter().GetResult();

        var address = application.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()?
            .Addresses.FirstOrDefault();

        if (address is null || !Uri.TryCreate(address, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("Kestrel did not report the address it bound.");

        return new(application, uri, token);
    }
}