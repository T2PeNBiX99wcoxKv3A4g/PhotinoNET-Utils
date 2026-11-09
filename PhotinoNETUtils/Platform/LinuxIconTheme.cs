using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using Serilog;

namespace PhotinoNetUtils.Platform;

/// <summary>
///     libayatana-appindicator expects a theme icon name, not an absolute file path. When an app
///     hands in an extracted icon file, copy it into the user's hicolor theme and return the name
///     AppIndicator can actually resolve. Existing theme names are passed through unchanged.
/// </summary>
[SupportedOSPlatform("linux")]
internal static class LinuxIconTheme
{
    private const string Fallback = "file-manager";

    public static string Resolve(string appId, string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
            return Fallback;

        if (!File.Exists(iconPath))
            return iconPath;

        try
        {
            var name = Sanitize(appId);
            if (name.Length == 0)
                return iconPath;

            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "icons", "hicolor");
            var directory = Path.Combine(root, "256x256", "apps");
            Directory.CreateDirectory(directory);

            var target = Path.Combine(directory, name + ".png");
            var bytes = File.ReadAllBytes(iconPath);

            if (!File.Exists(target) || !File.ReadAllBytes(target).SequenceEqual(bytes))
                File.WriteAllBytes(target, bytes);

            var index = Path.Combine(root, "index.theme");
            if (!File.Exists(index) && File.Exists("/usr/share/icons/hicolor/index.theme"))
                File.Copy("/usr/share/icons/hicolor/index.theme", index, true);

            RefreshCache(root);
            return name;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not install the tray icon into the icon theme");
            return iconPath;
        }
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value.ToLowerInvariant())
            if (char.IsLetterOrDigit(character) || character is '-' or '_')
                builder.Append(character);

        return builder.ToString();
    }

    private static void RefreshCache(string root)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "gtk-update-icon-cache",
                ArgumentList =
                {
                    "-f",
                    "-t",
                    root
                },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            process?.WaitForExit(2000);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not refresh the GTK icon cache");
        }
    }
}