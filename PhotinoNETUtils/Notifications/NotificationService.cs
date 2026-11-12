using JetBrains.Annotations;
using Photino.NET;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Utils;
using Serilog;

namespace PhotinoNetUtils.Notifications;

/// <summary>
///     Shows the operating system's own notification for a Photino application: a toast on Windows,
///     libnotify on Linux.
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
    private static PhotinoWindow? _window;

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
    ///     Starts delivering notifications through <paramref name="window" /> and sends whatever
    ///     waited for it. Call this once the native window exists.
    /// </summary>
    public static void Attach(PhotinoWindow window)
    {
        PendingNotification[] queued;

        lock (Gate)
        {
            _window = window;
            queued = [.. Pending.Values];
            Pending.Clear();
        }

        foreach (var notification in queued)
            Send(window, notification.Title, notification.Message);
    }

    /// <summary>Stops delivering; notifications raised after this, or still waiting, are dropped.</summary>
    public static void Detach()
    {
        lock (Gate)
        {
            _window = null;
            Pending.Clear();
        }
    }

    /// <summary>
    ///     Shows a notification, unless the same <paramref name="key" /> was shown within
    ///     <see cref="Debounce" />. Before <see cref="Attach" /> the newest message per key waits.
    /// </summary>
    public static void Notify(string key, string title, string message)
    {
        var now = Environment.TickCount64;
        PhotinoWindow? target = null;
        var queued = false;

        lock (Gate)
        {
            if (Last.TryGetValue(key, out var last) &&
                now - last < (long)_debounce.TotalMilliseconds)
                return;

            Last[key] = now;
            Prune(now);

            if (_window is { } window)
                target = window;
            else
            {
                Pending[key] = new(title, message);
                TrimPending();
                queued = true;
            }
        }

        if (!queued)
            Send(target!, title, message);
    }

    /// <summary>Shows a notification keyed by its own title and message, so repeats are still debounced.</summary>
    public static void Notify(string title, string message) =>
        Notify($"{title}\u0000{message}", title, message);

    private static void Send(PhotinoWindow window, string title, string message) =>
        Try.Run(() => window.SendNotification(title, message))
            .OnFailure(ex => Log.Warning(ex, "Could not show a notification"));

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

    private readonly record struct PendingNotification(string Title, string Message);
}