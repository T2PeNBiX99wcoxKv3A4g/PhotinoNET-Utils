using System.Diagnostics;
using Serilog;

namespace PhotinoNetUtils.Notifications;

/// <summary>
///     Shows a notification with <c>notify-send</c> and reports a click through its default action.
/// </summary>
/// <remarks>
///     <c>--wait</c> keeps the process around until the user acts on the notification, and
///     <c>--action default=…</c> makes the body clickable; the action id then arrives on stdout. A
///     notification daemon without actions simply never prints it.
/// </remarks>
internal static class LinuxToast
{
    public static void Show(string appName, string title, string message, NotificationOptions? options,
        Action<string> activated)
    {
        try
        {
            var start = new ProcessStartInfo("notify-send")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            start.ArgumentList.Add("--app-name");
            start.ArgumentList.Add(string.IsNullOrWhiteSpace(appName) ? "Photino" : appName);

            if (options?.ImagePath is { Length: > 0 } image)
            {
                start.ArgumentList.Add("--icon");
                start.ArgumentList.Add(image);
            }

            start.ArgumentList.Add("--action");
            start.ArgumentList.Add($"default={options?.ButtonText ?? "Open"}");
            start.ArgumentList.Add("--wait");
            start.ArgumentList.Add(title);
            start.ArgumentList.Add(message);

            var process = Process.Start(start);
            if (process is null)
                return;

            var clicked = options?.Action ?? "";

            _ = Task.Run(async () =>
            {
                try
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    if (output.Trim().Equals("default", StringComparison.OrdinalIgnoreCase))
                        activated(clicked);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Could not read the notification result");
                }
                finally
                {
                    process.Dispose();
                }
            });
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not show a Linux notification");
        }
    }
}