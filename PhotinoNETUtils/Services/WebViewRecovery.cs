using Photino.NET;
using PhotinoNetUtils.Messaging;
using JetBrains.Annotations;
using Serilog;

namespace PhotinoNetUtils.Services;

/// <summary>
///     Watches the app's main Photino page for its first render and for a small heartbeat from the
///     shared bridge. A page that never starts, or whose renderer goes blank later, is navigated to
///     its original URL again. The tray can call <see cref="Reload"/> for a manual retry.
/// </summary>
[PublicAPI]
public sealed class WebViewRecovery : IDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan StableRun = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);
    private const int AutomaticReloadLimit = 3;

    private readonly object _gate = new();
    private readonly PhotinoWindow _window;
    private readonly string _url;
    private readonly Timer _timer;
    private DateTimeOffset _lastLoad = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastHeartbeat;
    private DateTimeOffset _readySince;
    private bool _ready;
    private bool _reloading;
    private bool _disposed;
    private bool _automaticRecoveryDisabled;
    private bool _blankUiDetected;
    private int _blankHeartbeats;
    private int _automaticReloads;

    private WebViewRecovery(PhotinoWindow window, string url)
    {
        _window = window;
        _url = url;
        NativeBridge.MessageReceived += OnMessageAsync;
        _timer = new Timer(Check, null, CheckInterval, CheckInterval);
    }

    /// <summary>Starts watching one main window. Call before <c>PhotinoWindow.Load</c>.</summary>
    public static WebViewRecovery Attach(PhotinoWindow window, string url)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        return new WebViewRecovery(window, url);
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
            _lastHeartbeat = default;
            _automaticReloads = 0;
            _automaticRecoveryDisabled = false;
        }

        return Navigate("manual");
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

    private Task<NativeResponse?> OnMessageAsync(NativeContext context)
    {
        if (!ReferenceEquals(context.Window, _window)) return Task.FromResult<NativeResponse?>(null);

        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (_disposed) return Task.FromResult<NativeResponse?>(null);

            if (context.Request.Type == "app.ready")
            {
                _ready = true;
                _readySince = now;
                _lastHeartbeat = now;
                _blankHeartbeats = 0;
                _blankUiDetected = false;
            }
            else if (context.Request.Type == "webview.heartbeat" && _ready)
            {
                _lastHeartbeat = now;
                var blank = context.Request.Payload is { ValueKind: System.Text.Json.JsonValueKind.Object } payload
                    && payload.TryGetProperty("blank", out var value)
                    && value.ValueKind == System.Text.Json.JsonValueKind.True;
                _blankHeartbeats = blank ? _blankHeartbeats + 1 : 0;
                _blankUiDetected = _blankHeartbeats >= 3;
                if (_automaticReloads > 0 && now - _readySince >= StableRun)
                    _automaticReloads = 0;
            }
        }

        return Task.FromResult<NativeResponse?>(null);
    }

    private void Check(object? state)
    {
        string? reason = null;

        lock (_gate)
        {
            if (_disposed || _reloading || _automaticRecoveryDisabled) return;

            var now = DateTimeOffset.UtcNow;
            if (_blankUiDetected)
                reason = "the document root is still empty";
            else if (!_ready && now - _lastLoad >= StartupTimeout)
                reason = "the page did not report its first render";
            else if (_ready && now - _lastHeartbeat >= HeartbeatTimeout)
                reason = "the page heartbeat stopped";

            if (reason is null) return;
            if (_automaticReloads >= AutomaticReloadLimit)
            {
                Log.Error("WebView recovery stopped after {Count} retries ({Reason})", _automaticReloads, reason);
                _automaticRecoveryDisabled = true;
                return;
            }

            _automaticReloads++;
            _reloading = true;
            _ready = false;
            _lastLoad = now;
            _lastHeartbeat = default;
        }

        Log.Warning("Reloading the WebView because {Reason}; retry {Retry}/{Limit}", reason,
            _automaticReloads, AutomaticReloadLimit);
        Navigate(reason);
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
