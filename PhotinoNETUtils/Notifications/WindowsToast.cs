using Serilog;
#if WINDOWS_DESKTOP
using Windows.ApplicationModel.Activation;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
#endif

namespace PhotinoNetUtils.Notifications;

/// <summary>
///     Shows a Windows toast that reports the click back. Photino's own
///     <c>SendNotification</c> hands the text to the same Windows API but keeps the activation for
///     itself, so a click there goes nowhere.
/// </summary>
/// <remarks>
///     The app id is the one Photino registered when the window was created — the window title,
///     unless <c>NotificationRegistrationId</c> overrides it — which is also the Start Menu shortcut
///     Windows uses to attribute the toast.
/// </remarks>
internal static class WindowsToast
{
    /// <summary>Windows raises the click later, so the notification has to stay alive until then.</summary>
    private static readonly List<object> Live = [];

    private static readonly Lock Gate = new();

    public static bool TryShow(string appId, string title, string message, NotificationOptions? options,
        Action<string> activated)
    {
#if WINDOWS_DESKTOP
        try
        {
            var xml = new XmlDocument();
            xml.LoadXml("<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                        "<toast><visual><binding template=\"ToastGeneric\"><text/><text/></binding></visual></toast>");

            var root = (XmlElement)xml.SelectSingleNode("/toast")!;

            // The launch argument is what a click on the body hands back: the page to open.
            root.SetAttribute("launch", options?.Action ?? "");
            // "long" keeps the toast on screen for about 25 seconds; the default disappears in a few.
            root.SetAttribute("duration", "long");

            var binding = (XmlElement)xml.SelectSingleNode("/toast/visual/binding")!;
            var texts = binding.GetElementsByTagName("text");
            texts[0].AppendChild(xml.CreateTextNode(title));
            texts[1].AppendChild(xml.CreateTextNode(message));

            if (options?.Attribution is { Length: > 0 } attribution)
            {
                var element = xml.CreateElement("text");
                element.SetAttribute("placement", "attribution");
                element.AppendChild(xml.CreateTextNode(attribution));
                binding.AppendChild(element);
            }

            if (options?.ImagePath is { Length: > 0 } image)
            {
                var element = xml.CreateElement("image");
                element.SetAttribute("placement", "appLogoOverride");
                element.SetAttribute("hint-crop", "circle");
                element.SetAttribute("src", new Uri(Path.GetFullPath(image)).AbsoluteUri);
                binding.AppendChild(element);
            }

            if (options is { ButtonText: { Length: > 0 } button, Action: { Length: > 0 } target })
            {
                var actions = xml.CreateElement("actions");
                var element = xml.CreateElement("action");
                element.SetAttribute("content", button);
                element.SetAttribute("arguments", target);
                element.SetAttribute("activationType", "foreground");
                actions.AppendChild(element);
                root.AppendChild(actions);
            }

            var notifier = ToastNotificationManager.CreateToastNotifier(appId);
            var toast = new ToastNotification(xml);

            toast.Activated += (_, args) =>
            {
                Forget(toast);
                activated(args is ToastNotificationActivatedEventArgs eventArgs &&
                          eventArgs.Argument is { Length: > 0 } argument
                    ? argument
                    : options?.Action ?? "");
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
            Log.Warning(ex, "Could not create a Windows toast");
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