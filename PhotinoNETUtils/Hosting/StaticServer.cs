using System.Reflection;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Serilog;

namespace PhotinoNetUtils.Hosting;

/// <summary>
///     The local Kestrel instance that serves the built interface to a Photino web view.
///     It binds to port 0, so the OS hands out a port that is really free instead of every Photino
///     app racing for 8000 — which is what <c>Photino.NET.Server</c> does, with a scan that can miss
///     an occupied port and then fail to bind.
/// </summary>
public sealed class StaticServer : IDisposable
{
    private readonly WebApplication _application;

    private StaticServer(WebApplication application, Uri address)
    {
        _application = application;
        Address = address;
    }

    /// <summary>The address Kestrel actually bound, e.g. <c>http://127.0.0.1:54321</c>.</summary>
    [PublicAPI]
    public Uri Address { get; }

    /// <summary>The address without a trailing slash, ready for composing page URLs.</summary>
    public string BaseUrl => Address.GetLeftPart(UriPartial.Authority);

    public void Dispose() => _application.DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>
    ///     Starts the server and waits until Kestrel reports a real port. The content root is a
    ///     folder owned by the app, so the server cannot drop a <c>wwwroot</c> next to the executable.
    ///     Embedded files are read from <paramref name="assembly" /> under
    ///     <paramref name="embeddedResourcePath" />.
    /// </summary>
    /// <param name="contentRoot">The folder Kestrel treats as its content root.</param>
    /// <param name="port">
    ///     The port to serve on, or 0 to let the OS pick one. A web view stores local storage per
    ///     origin, so an app that keeps interface state in the page needs the same port every time;
    ///     when the preferred one is taken, a free one is used instead.
    /// </param>
    /// <param name="assembly">The assembly carrying the embedded interface.</param>
    /// <param name="embeddedResourcePath">Where that interface lives in the assembly.</param>
    public static StaticServer Start(
        string contentRoot,
        int port = 0,
        Assembly? assembly = null,
        string embeddedResourcePath = "Resources/wwwroot")
    {
        if (port > 0)
            try
            {
                return StartOn(contentRoot, port, assembly, embeddedResourcePath);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Port {Port} is not available; serving the interface on a free one", port);
            }

        return StartOn(contentRoot, 0, assembly, embeddedResourcePath);
    }

    private static StaticServer StartOn(
        string contentRoot,
        int port,
        Assembly? assembly,
        string embeddedResourcePath)
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
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

        // Files on disk win, so a build can be overridden without touching the embedded copy.
        builder.Environment.WebRootFileProvider = new CompositeFileProvider(
            builder.Environment.WebRootFileProvider,
            new ManifestEmbeddedFileProvider(
                assembly ?? Assembly.GetEntryAssembly() ?? Assembly.GetCallingAssembly(),
                embeddedResourcePath));

        var application = builder.Build();
        application.UseDefaultFiles();
        application.UseStaticFiles(new StaticFileOptions
        {
            DefaultContentType = "text/plain"
        });

        try
        {
            application.StartAsync().GetAwaiter().GetResult();
        }
        catch
        {
            application.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }

        var address = application.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()?
            .Addresses.FirstOrDefault();

        if (address is null || !Uri.TryCreate(address, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("Kestrel did not report the address it bound.");

        return new(application, uri);
    }
}