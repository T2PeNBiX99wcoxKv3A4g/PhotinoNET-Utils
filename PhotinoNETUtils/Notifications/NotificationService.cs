using JetBrains.Annotations;
using Photino.NET;
using Serilog;

namespace PhotinoNetUtils.Notifications;

/// <summary>
///     Shows the operating system's own notification: a Windows toast or <c>notify-send</c> on Linux,
///     created by this library rather than Photino so a click can be reported back.
/// </summary>
/// <remarks>
///     <para>
///         The notification is handed to Photino's native layer, which registers the app with the
///         platform's notification centre while creating the window. That registration is why the
///         window has to exist: <see cref="Attach" /> starts delivery once it does.
///     </para>
///     <para>
///         A notification raised before the window exists is not lost. The newest message of each key
///         waits and is delivered by <see cref="Attach" />, so a background service can announce a
///         startup failure before the window is on screen.
///     </para>
///     <para>
///         Repeats of the same <c>key</c> are suppressed for <see cref="Debounce" />, which keeps one
///         event from producing a burst of identical toasts.
///     </para>
/// </remarks>
[PublicAPI]
public static class NotificationService
{
    /// <summary>Beyond this many keys the debounce table is pruned; a key is cheap, a leak is not.</summary>
    private const int MaxKeys = 512;

    /// <summary>Enough for the handful of events a startup can raise, without growing forever.</summary>
    private const int MaxPending = 32;

    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, long> Last = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, PendingNotification> Pending = new(StringComparer.Ordinal);

    private static TimeSpan _debounce = TimeSpan.FromSeconds(15);
    private static string? _appId;

    /// <summary>How long the same key is ignored after it was shown. Defaults to 15 seconds.</summary>
    public static TimeSpan Debounce
    {
        get
        {
            lock (Gate)
                return _debounce;
        }
        set
        {
            lock (Gate)
                _debounce = value < TimeSpan.Zero ? TimeSpan.Zero : value;
        }
    }

    /// <summary>
    ///     Takes the id Windows attributes notifications to from <paramref name="window" /> and sends
    ///     whatever waited for it. Call this once the native window exists.
    /// </summary>
    public static void Attach(PhotinoWindow window) =>
        Attach(window.NotificationRegistrationId is { Length: > 0 } id ? id : window.Title);

    /// <summary>
    ///     Starts delivering with an explicit app id and sends whatever waited for it. The
    ///     <see cref="PhotinoWindow" /> overload reads the id from the window instead.
    /// </summary>
    public static void Attach(string appId)
    {
        PendingNotification[] queued;

        lock (Gate)
        {
            _appId = appId;
            queued = [.. Pending.Values];
            Pending.Clear();
        }

        foreach (var notification in queued)
            Send(notification.Title, notification.Message, notification.Options);
    }

    /// <summary>Stops delivering; notifications raised after this, or still waiting, are dropped.</summary>
    public static void Detach()
    {
        lock (Gate)
        {
            _appId = null;
            Pending.Clear();
        }
    }

    /// <summary>
    ///     Raised when a shown notification is clicked. The argument is the action it was created
    ///     with, so an app can open the page it belongs to.
    /// </summary>
    public static event Action<string>? Activated;

    /// <summary>
    ///     Shows a notification, unless the same <paramref name="key" /> was shown within
    ///     <see cref="Debounce" />. Before <see cref="Attach" /> the newest message per key waits.
    ///     <paramref name="action" /> is handed back through <see cref="Activated" /> when it is
    ///     clicked; use the overload with <see cref="NotificationOptions" /> for the richer form.
    /// </summary>
    public static void Notify(string key, string title, string message, string? action = null) =>
        Notify(key, title, message, action is null ? null : new NotificationOptions { Action = action });

    /// <summary>Shows a notification with the optional image, attribution and button.</summary>
    public static void Notify(string key, string title, string message, NotificationOptions? options)
    {
        var now = Environment.TickCount64;
        var ready = false;

        lock (Gate)
        {
            if (Last.TryGetValue(key, out var last) &&
                now - last < (long)_debounce.TotalMilliseconds)
                return;

            Last[key] = now;
            Prune(now);

            if (_appId is null)
            {
                Pending[key] = new(title, message, options);
                TrimPending();
            }
            else
                ready = true;
        }

        if (ready)
            Send(title, message, options);
    }

    /// <summary>Shows a notification keyed by its own title and message, so repeats are still debounced.</summary>
    public static void Notify(string title, string message, string? action = null) =>
        Notify($"{title}\u0000{message}", title, message, action);

    /// <summary>Shows a notification keyed by its own title and message, with the richer options.</summary>
    public static void Notify(string title, string message, NotificationOptions? options) =>
        Notify($"{title}\u0000{message}", title, message, options);

    private static void Send(string title, string message, NotificationOptions? options)
    {
        var appId = _appId ?? "";

        if (OperatingSystem.IsWindows())
            WindowsToast.TryShow(appId, title, message, options, Clicked);
        else if (OperatingSystem.IsLinux())
            LinuxToast.Show(appId, title, message, options, Clicked);
    }

    private static void Clicked(string action)
    {
        try
        {
            Activated?.Invoke(action);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "A notification click handler failed");
        }
    }

    /// <summary>Drops keys that are older than the debounce window once the table grows large.</summary>
    private static void Prune(long now)
    {
        if (Last.Count <= MaxKeys)
            return;

        var threshold = now - (long)_debounce.TotalMilliseconds;

        foreach (var key in Last.Where(pair => pair.Value < threshold).Select(pair => pair.Key).ToArray())
            Last.Remove(key);
    }

    private static void TrimPending()
    {
        while (Pending.Count > MaxPending)
            Pending.Remove(Pending.Keys.First());
    }

    private readonly record struct PendingNotification(string Title, string Message, NotificationOptions? Options);
}