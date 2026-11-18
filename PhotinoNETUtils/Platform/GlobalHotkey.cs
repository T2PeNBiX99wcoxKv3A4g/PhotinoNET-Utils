using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JetBrains.Annotations;
using Serilog;

namespace PhotinoNetUtils.Platform;

/// <summary>
///     Global key combinations that call back while the app runs. It polls the keyboard instead of
///     registering a hotkey, so it also works when another process owns the foreground window. The
///     callback decides what the combinations do, which keeps the helper free of any window logic.
/// </summary>
[PublicAPI]
public static partial class GlobalHotkey
{
    private static readonly Dictionary<string, int> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = 0x11,
        ["control"] = 0x11,
        ["shift"] = 0x10,
        ["alt"] = 0x12,
        ["win"] = 0x5B,
        ["space"] = 0x20,
        ["enter"] = 0x0D,
        ["return"] = 0x0D,
        ["tab"] = 0x09,
        ["escape"] = 0x1B,
        ["esc"] = 0x1B,
        ["insert"] = 0x2D,
        ["delete"] = 0x2E,
        ["home"] = 0x24,
        ["end"] = 0x23,
        ["pageup"] = 0x21,
        ["pagedown"] = 0x22,
        ["up"] = 0x26,
        ["down"] = 0x28,
        ["left"] = 0x25,
        ["right"] = 0x27
    };

    private static CancellationTokenSource? _stop;

    /// <summary>
    ///     Starts polling <paramref name="combinations" />, several of them separated by "|", and calls
    ///     <paramref name="onPressed" /> whenever one of them is pressed. An empty value stops the
    ///     polling, so the same call turns the shortcut off again.
    /// </summary>
    public static void Apply(string? combinations, Action onPressed) =>
        Apply(Split(combinations), onPressed);

    /// <summary>Starts polling every combination; each one calls <paramref name="onPressed" /> on its own.</summary>
    public static void Apply(IEnumerable<string> combinations, Action onPressed)
    {
        Stop();

        var lines = combinations
            .SelectMany(Split)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (lines.Length == 0)
            return;

        if (OperatingSystem.IsLinux())
        {
            StartLinux(lines, onPressed);
            return;
        }

        if (!OperatingSystem.IsWindows())
            return;

        var keys = lines.Select(WindowsKeys).OfType<int[]>().ToArray();
        if (keys.Length == 0)
            return;

        _stop = new();
        StartPolling(keys, _stop.Token, onPressed);
    }

    /// <summary>The "|" separated forms of one stored value, with the empty ones dropped.</summary>
    private static IEnumerable<string> Split(string? value) =>
        (value ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The virtual key codes of one combination, or null when a part is not a key this helper knows.</summary>
    [SupportedOSPlatform("windows")]
    private static int[]? WindowsKeys(string combination)
    {
        var keys = new List<int>();

        foreach (var part in combination.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (part.Length == 1 && char.IsLetterOrDigit(part[0]))
                keys.Add(char.ToUpperInvariant(part[0]));
            else if (part.StartsWith('f') && int.TryParse(part[1..], out var function) && function is >= 1 and <= 24)
                keys.Add(0x70 + function - 1);
            else if (Keys.TryGetValue(part, out var code))
                keys.Add(code);
            else
                return null;

        return keys.Count == 0 ? null : [.. keys];
    }

    [SupportedOSPlatform("windows")]
    private static void StartPolling(int[][] combinations, CancellationToken token, Action onPressed) =>
        _ = Task.Run(() => Poll(combinations, token, onPressed), token);

    /// <summary>The polling loop; <c>GetAsyncKeyState</c> is a Win32 call, so it is Windows only.</summary>
    [SupportedOSPlatform("windows")]
    private static void Poll(int[][] combinations, CancellationToken token, Action onPressed)
    {
        // Every combination keeps its own state, so holding one does not swallow another.
        var wasDown = new bool[combinations.Length];

        while (!token.IsCancellationRequested)
        {
            try
            {
                for (var index = 0; index < combinations.Length; index++)
                {
                    var down = combinations[index].All(key => (GetAsyncKeyState(key) & 0x8000) != 0);

                    if (down && !wasDown[index])
                    {
                        wasDown[index] = true;
                        onPressed();
                    }
                    else if (!down)
                        wasDown[index] = false;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not poll the global hotkey");
            }

            Thread.Sleep(80);
        }
    }

    /// <summary>Stops every running combination.</summary>
    public static void Stop()
    {
        _stop?.Cancel();
        _stop?.Dispose();
        _stop = null;
    }

    /// <summary>
    ///     X11 keeps the keyboard state in the server, so <c>XQueryKeymap</c> polls it the same way
    ///     the Windows loop uses <c>GetAsyncKeyState</c>. A Wayland session only answers while
    ///     XWayland has the focus, so the combination may stay inert there; nothing fails.
    /// </summary>
    [SupportedOSPlatform("linux")]
    private static void StartLinux(IEnumerable<string> combinations, Action onPressed)
    {
        var names = combinations.Select(LinuxNames).OfType<string[]>().ToArray();
        if (names.Length == 0)
            return;

        _stop = new();
        var token = _stop.Token;

        _ = Task.Run(() =>
        {
            // A Wayland-only machine may not even have libX11; a missing hotkey is not an error.
            var display = IntPtr.Zero;

            try
            {
                display = XOpenDisplay(null);
                if (display == IntPtr.Zero)
                    return;

                var keys = names
                    .Select(parts => parts.Select(name => XKeysymToKeycode(display, XStringToKeysym(name))).ToArray())
                    .Where(codes => codes.All(code => code != 0))
                    .ToArray();

                if (keys.Length == 0)
                    return;

                var wasDown = new bool[keys.Length];
                var state = new byte[32];

                while (!token.IsCancellationRequested)
                {
                    XQueryKeymap(display, state);

                    for (var index = 0; index < keys.Length; index++)
                    {
                        var down = keys[index].All(code => (state[code >> 3] & 1 << (code & 7)) != 0);

                        if (down && !wasDown[index])
                        {
                            wasDown[index] = true;
                            onPressed();
                        }
                        else if (!down)
                            wasDown[index] = false;
                    }

                    Thread.Sleep(80);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not poll the global hotkey");
            }
            finally
            {
                if (display != IntPtr.Zero)
                    XCloseDisplay(display);
            }
        }, token);
    }

    /// <summary>The X11 keysym names of one combination, or null when a part has none.</summary>
    [SupportedOSPlatform("linux")]
    private static string[]? LinuxNames(string combination)
    {
        var names = new List<string>();

        foreach (var part in combination.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (LinuxKeyName(part) is not { } name)
                return null;

            names.Add(name);
        }

        return names.Count == 0 ? null : [.. names];
    }

    /// <summary>The X11 keysym name for one part of the combination.</summary>
    private static string? LinuxKeyName(string part)
    {
        if (part.Length == 1 && char.IsLetterOrDigit(part[0]))
            return char.ToLowerInvariant(part[0]).ToString();

        if (part.Length is >= 2 and <= 3 && part[0] is 'f' or 'F'
                                         && int.TryParse(part[1..], out var function) && function is >= 1 and <= 24)
            return $"F{function}";

        return part.ToLowerInvariant() switch
        {
            "ctrl" or "control" => "Control_L",
            "shift" => "Shift_L",
            "alt" => "Alt_L",
            "win" or "meta" or "super" => "Super_L",
            "space" => "space",
            "enter" or "return" => "Return",
            "tab" => "Tab",
            "escape" or "esc" => "Escape",
            "insert" => "Insert",
            "delete" => "Delete",
            "home" => "Home",
            "end" => "End",
            "pageup" => "Prior",
            "pagedown" => "Next",
            "up" => "Up",
            "down" => "Down",
            "left" => "Left",
            "right" => "Right",
            _ => null
        };
    }

    [LibraryImport("libX11.so.6", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr XOpenDisplay(string? name);

    [LibraryImport("libX11.so.6")]
    private static partial int XCloseDisplay(IntPtr display);

    [LibraryImport("libX11.so.6", StringMarshalling = StringMarshalling.Utf8)]
    private static partial ulong XStringToKeysym(string name);

    [LibraryImport("libX11.so.6")]
    private static partial byte XKeysymToKeycode(IntPtr display, ulong keysym);

    [LibraryImport("libX11.so.6")]
    private static partial int XQueryKeymap(IntPtr display, byte[] keys);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
