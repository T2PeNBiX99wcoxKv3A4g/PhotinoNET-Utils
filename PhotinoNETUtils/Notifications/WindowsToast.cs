#if WINDOWS_DESKTOP
using Windows.ApplicationModel.Activation;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
#endif
using Serilog;

namespace PhotinoNetUtils.Notifications;

/// <summary>
///     Shows a Windows toast that reports the click back. Photino's own
///     <c>SendNotification</c> hands the text to the same Windows API but keeps the activation for
///     itself, so a click there goes nowhere.
/// </summary>
/// <remarks>
///     The app id is the one Photino registered when the window was created — the window title,
///     unless <c>NotificationRegistrationId</c> overrides it — which is also the Start Menu shortcut
///     Windows uses to attribute the toast. Without that registration, or on another OS, this class
///     reports false and the caller falls back to Photino.
/// </remarks>
internal static class WindowsToast
{
    /// <summary>Windows raises the click later, so the notification has to stay alive until then.</summary>
    private static readonly List<object> Live = [];
    private static readonly Lock Gate = new();

    public static bool TryShow(string appId, string title, string message, string? action,
        Action<string> activated)
    {
#if WINDOWS_DESKTOP
        try
        {
            var notifier = ToastNotificationManager.CreateToastNotifier(appId);
            var xml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
            var root = (XmlElement)xml.SelectSingleNode("/toast")!;

            // The launch argument is what the click hands back: the page the app should open.
            root.SetAttribute("launch", action ?? "");
            // "long" keeps the toast on screen for about 25 seconds; the default disappears in a few.
            root.SetAttribute("duration", "long");

            var texts = xml.GetElementsByTagName("text");
            texts[0].AppendChild(xml.CreateTextNode(title));
            texts[1].AppendChild(xml.CreateTextNode(message));

            var toast = new ToastNotification(xml);

            toast.Activated += (_, args) =>
            {
                Forget(toast);
                activated(args is ToastNotificationActivatedEventArgs eventArgs ? eventArgs.Argument : "");
            };
            toast.Dismissed += (_, _) => Forget(toast);
            toast.Failed += (_, args) =>
            {
                Forget(toast);
                Log.Warning("The Windows toast failed with {Reason}", args.ErrorCode);
            };

            lock (Gate)
                Live.Add(toast);

            notifier.Show(toast);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not create a Windows toast; the Photino notification is used instead");
            return false;
        }
#else
        return false;
#endif
    }

    private static void Forget(object toast)
    {
        lock (Gate)
            Live.Remove(toast);
    }
}
