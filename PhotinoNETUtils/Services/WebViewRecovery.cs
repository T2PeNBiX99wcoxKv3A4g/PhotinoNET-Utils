using JetBrains.Annotations;
using Photino.NET;
using PhotinoNetUtils.Messaging;
using Serilog;

namespace PhotinoNetUtils.Services;

/// <summary>
///     Watches the app's main Photino page for its first render. A page that never starts is navigated
///     to its original URL again, and the tray can call <see cref="Reload" /> for a manual retry.
/// </summary>
/// <remarks>
///     Once the page has reported ready it is left alone: this is a startup watchdog, not a renderer
///     monitor, so a later blank render never navigates away from a page the user is working in.
/// </remarks>
[PublicAPI]
public sealed class WebViewRecovery : IDisposable
{
    private const int AutomaticReloadLimit = 3;
    private const string StartupReason = "the page did not report its first render";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);

    private readonly object _gate = new();
    private readonly Timer _timer;
    private readonly string _url;
    private readonly PhotinoWindow _window;
    private bool _automaticRecoveryDisabled;
    private int _automaticReloads;
    private bool _disposed;
    private DateTimeOffset _lastLoad = DateTimeOffset.UtcNow;
    private bool _ready;
    private bool _reloading;

    private WebViewRecovery(PhotinoWindow window, string url)
    {
        _window = window;
        _url = url;
        NativeBridge.MessageReceived += OnMessageAsync;
        _timer = new(Check, null, CheckInterval, CheckInterval);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _timer.Dispose();
        NativeBridge.MessageReceived -= OnMessageAsync;
    }

    /// <summary>Starts watching one main window. Call before <c>PhotinoWindow.Load</c>.</summary>
    public static WebViewRecovery Attach(PhotinoWindow window, string url)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        return new(window, url);
    }

    /// <summary>Reloads the configured URL now; returns false if Photino refused the navigation.</summary>
    public bool Reload()
    {
        lock (_gate)
        {
            if (_disposed || _reloading) return false;
            _reloading = true;
            _ready = false;
            _lastLoad = DateTimeOffset.UtcNow;
            _automaticReloads = 0;
            _automaticRecoveryDisabled = false;
        }

        return Navigate("manual");
    }

    private Task<NativeResponse?> OnMessageAsync(NativeContext context)
    {
        if (!ReferenceEquals(context.Window, _window) || context.Request.Type != "app.ready")
            return Task.FromResult<NativeResponse?>(null);

        lock (_gate)
            if (!_disposed)
                _ready = true;

        return Task.FromResult<NativeResponse?>(null);
    }

    private void Check(object? state)
    {
        lock (_gate)
        {
            if (_disposed || _reloading || _automaticRecoveryDisabled || _ready) return;

            var now = DateTimeOffset.UtcNow;
            if (now - _lastLoad < StartupTimeout) return;

            if (_automaticReloads >= AutomaticReloadLimit)
            {
                Log.Error("WebView recovery stopped after {Count} retries ({Reason})", _automaticReloads,
                    StartupReason);
                _automaticRecoveryDisabled = true;
                return;
            }

            _automaticReloads++;
            _reloading = true;
            _lastLoad = now;
        }

        Log.Warning("Reloading the WebView because {Reason}; retry {Retry}/{Limit}", StartupReason,
            _automaticReloads, AutomaticReloadLimit);
        Navigate(StartupReason);
    }

    private bool Navigate(string reason)
    {
        try
        {
            _window.Invoke(() => _window.Load(_url));
            Log.Information("WebView navigated to {Url} ({Reason})", _url, reason);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not reload the WebView ({Reason})", reason);
            return false;
        }
        finally
        {
            lock (_gate) _reloading = false;
        }
    }
}