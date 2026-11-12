using JetBrains.Annotations;
using Photino.NET;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Messaging;
using PhotinoNetUtils.Platform;
using PhotinoNetUtils.Utils;
using Serilog;

namespace PhotinoNetUtils.Hosting;

/// <summary>
///     Pushes unsolicited events to the interface and answers the standard window commands the
///     front-end bridge sends.
/// </summary>
/// <remarks>
///     <para>
///         Photino aborts if it is asked to send a message while the web view is still loading, so an
///         event raised before the page reports <c>app.ready</c> waits here. Only the newest event of
///         each type is kept: a stale instance list is useless once a newer one exists.
///     </para>
///     <para>
///         <see cref="Attach" /> must be called once the native window exists and before the page is
///         loaded (<c>WindowManager.CreateMainWindow</c> is the natural place). The window commands are
///         the ones the shared bridge sends from <c>useWindowControls</c>, <c>useWindowDrag</c> and
///         <c>ResizeHandles</c>; the app only needs to keep its own close-to-tray policy.
///     </para>
/// </remarks>
[PublicAPI]
public static class WebViewEvents
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, string> Pending = new(StringComparer.Ordinal);

    private static PhotinoWindow? _window;
    private static bool _ready;

    static WebViewEvents() => NativeBridge.MessageReceived += HandleMessageAsync;

    /// <summary>Raised every time the page reports <c>app.ready</c>.</summary>
    public static event Action? Ready;

    /// <summary>Starts delivering through <paramref name="window" />; early events stay queued.</summary>
    public static void Attach(PhotinoWindow window)
    {
        lock (Gate)
        {
            _window = window;
            _ready = false;
        }
    }

    /// <summary>Stops delivering and drops anything still waiting.</summary>
    public static void Detach()
    {
        lock (Gate)
        {
            _window = null;
            _ready = false;
            Pending.Clear();
        }
    }

    /// <summary>
    ///     Pushes an event, or holds the newest one of its type until the page reports ready. Safe from
    ///     any thread, including before the window exists.
    /// </summary>
    public static void Notify(string type, object? data = null)
    {
        var message = Try.Run(() => NativeBridge.SerializeEvent(type, data)).GetOrNull();
        if (message is null)
            return;

        lock (Gate)
        {
            if (!_ready || _window is null)
            {
                Pending[type] = message;
                return;
            }
        }

        Send(message, type);
    }

    private static Task<NativeResponse?> HandleMessageAsync(NativeContext context) =>
        context.Request.Type switch
        {
            "app.ready" => Task.FromResult(MarkReady()),
            "window.minimize" => Run(() =>
            {
                if (_window is { } window)
                    window.Invoke(() => window.SetMinimized(true));
            }),
            "window.toggleMaximize" => Run(() =>
            {
                if (_window is { } window)
                    window.Invoke(() => ToggleMaximize(window));
            }),
            "window.close" => Run(() =>
            {
                if (_window is { } window)
                    window.Invoke(() => window.Close());
            }),
            "window.drag" => Run(() =>
            {
                if (_window is { } window)
                    window.Invoke(() => BeginDrag(window));
            }),
            "window.resize" => Run(() =>
            {
                if (_window is { } window)
                    window.Invoke(() => BeginResize(window, context));
            }),
            _ => Task.FromResult<NativeResponse?>(null)
        };

    private static NativeResponse? MarkReady()
    {
        KeyValuePair<string, string>[] pending;

        lock (Gate)
        {
            _ready = true;
            pending = [.. Pending];
            Pending.Clear();
        }

        foreach (var (type, message) in pending)
            Send(message, type);

        Ready?.Invoke();
        return null;
    }

    private static void ToggleMaximize(PhotinoWindow window)
    {
        if (OperatingSystem.IsWindows())
        {
            if (window.WindowHandle != IntPtr.Zero)
                Notify("window.state", new WindowStatePayload(WindowZoom.Toggle(window.WindowHandle)));

            return;
        }

        if (OperatingSystem.IsLinux())
        {
            Notify("window.state", new WindowStatePayload(LinuxWindow.ToggleMaximize(window.Title)));
            return;
        }

        var maximized = !window.Maximized;
        window.SetMaximized(maximized);
        Notify("window.state", new WindowStatePayload(maximized));
    }

    private static void BeginDrag(PhotinoWindow window)
    {
        if (OperatingSystem.IsWindows())
            WindowDrag.Begin(window.WindowHandle);
        else if (OperatingSystem.IsLinux())
            LinuxWindow.BeginDrag(window.Title);
    }

    private static void BeginResize(PhotinoWindow window, NativeContext context)
    {
        var direction = context.Payload<WindowResizeRequest>()?.Direction ?? "";

        if (OperatingSystem.IsWindows())
            WindowResize.Begin(window.WindowHandle, direction);
        else if (OperatingSystem.IsLinux())
            LinuxWindow.BeginResize(window.Title, direction);
    }

    private static Task<NativeResponse?> Run(Action action)
    {
        Try.Run(action).OnFailure(ex => Log.Warning(ex, "Window command failed"));
        return Task.FromResult<NativeResponse?>(null);
    }

    private static void Send(string message, string type)
    {
        if (_window is not { } window)
            return;

        Try.Run(() => window.Invoke(() => window.SendWebMessage(message)))
            .OnFailure(ex => Log.Warning(ex, "Failed to push {Type} to the interface", type));
    }
}
