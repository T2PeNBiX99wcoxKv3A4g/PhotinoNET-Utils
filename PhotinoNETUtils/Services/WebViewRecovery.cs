using System.Text.Json;
using JetBrains.Annotations;
using Photino.NET;
using PhotinoNetUtils.Messaging;
using Serilog;

namespace PhotinoNetUtils.Services;

/// <summary>
///     Watches the app's main Photino page for its first render and for a small heartbeat from the
///     shared bridge. A page that never starts, or whose renderer goes blank later, is navigated to
///     its original URL again. The tray can call <see cref="Reload" /> for a manual retry.
/// </summary>
[PublicAPI]
public sealed class WebViewRecovery : IDisposable
{
    private const int AutomaticReloadLimit = 3;
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan StableRun = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);

    private readonly object _gate = new();
    private readonly Timer _timer;
    private readonly string _url;
    private readonly PhotinoWindow _window;
    private bool _automaticRecoveryDisabled;
    private int _automaticReloads;
    private int _blankHeartbeats;
    private bool _blankUiDetected;
    private bool _disposed;
    private DateTimeOffset _lastHeartbeat;
    private DateTimeOffset _lastLoad = DateTimeOffset.UtcNow;
    private bool _ready;
    private DateTimeOffset _readySince;
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
            _lastHeartbeat = default;
            _automaticReloads = 0;
            _automaticRecoveryDisabled = false;
        }

        return Navigate("manual");
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
                var blank = context.Request.Payload is { ValueKind: JsonValueKind.Object } payload
                            && payload.TryGetProperty("blank", out var value)
                            && value.ValueKind == JsonValueKind.True;
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

            // Once the page has reported its first render it is considered healthy. A missing
            // heartbeat or an empty-looking heartbeat must not navigate away from a page the user
            // is already working in; the tray's Reload entry is the manual recovery path.
            if (_ready) return;

            var now = DateTimeOffset.UtcNow;
            if (now - _lastLoad >= StartupTimeout)
                reason = "the page did not report its first render";

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