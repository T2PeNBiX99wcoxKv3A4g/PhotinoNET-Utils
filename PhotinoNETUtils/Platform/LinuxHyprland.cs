using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Utils;
using Serilog;

namespace PhotinoNetUtils.Platform;

/// <summary>
///     Window geometry through Hyprland's command socket, for the two things Wayland does not let a
///     client do itself: change the size of its own window, and find the pointer.
/// </summary>
/// <remarks>
///     A resize request from a client is only a hint, and Hyprland ignores it for a floating window,
///     so such a window keeps whatever size it was mapped with. Asking the compositor to do it works,
///     and the command socket is <c>hyprctl</c> without spawning it. Dispatches are Lua expressions
///     there, so the calls below are written for Hyprland 0.5x; a compositor that does not understand
///     them answers with an error and nothing changes.
/// </remarks>
[PublicAPI]
[SupportedOSPlatform("linux")]
public static class LinuxHyprland
{
    /// <summary>Exported into every process a Hyprland session starts, and into nothing else.</summary>
    private const string InstanceVariable = "HYPRLAND_INSTANCE_SIGNATURE";

    private const string SocketName = ".socket.sock";

    /// <summary>The socket of this session, which never changes; null when this is not Hyprland.</summary>
    private static readonly Lazy<string?> CommandSocket = new(FindCommandSocket);

    /// <summary>Whether this session is Hyprland, and so whether anything here can work at all.</summary>
    public static bool IsAvailable => CommandSocket.Value is not null;

    /// <summary>
    ///     Resizes the toplevel with this exact title to <paramref name="width" /> by
    ///     <paramref name="height" /> and puts it back in the middle of the screen, since a
    ///     compositor-side resize grows the window from its top-left corner. False when Hyprland could
    ///     not be asked, in which case the caller has to settle for the hint it already gave.
    /// </summary>
    public static bool Resize(string title, int width, int height) =>
        Run(title, window =>
            Dispatch(window.Socket,
                     $"hl.dsp.window.resize({{ window = \"{window.Selector}\", x = {width}, y = {height}, relative = false }})") &&
            Dispatch(window.Socket, $"hl.dsp.window.center({{ window = \"{window.Selector}\" }})"));

    /// <summary>
    ///     Puts the toplevel with this exact title <paramref name="offset" /> pixels down and to the
    ///     right of the pointer, kept inside the monitor the pointer is on. Wayland hides the pointer
    ///     from clients, so the position a client asks for is meaningless and only the compositor can
    ///     place a window next to the pointer. False when Hyprland could not be asked.
    /// </summary>
    public static bool MoveNextToCursor(string title, int offset, int size) =>
        Run(title, window =>
        {
            if (Point(Ask(window.Socket, "j/cursorpos")) is not { } cursor)
                return false;

            var at = ClampToMonitor(Ask(window.Socket, "j/monitors"), cursor,
                                    (cursor.X + offset, cursor.Y + offset), size);

            return Dispatch(window.Socket,
                            $"hl.dsp.window.move({{ window = \"{window.Selector}\", x = {at.X}, y = {at.Y} }})");
        });

    private static string? FindCommandSocket()
    {
        if (Environment.GetEnvironmentVariable(InstanceVariable) is not { Length: > 0 } signature)
            return null;

        // Hyprland moved its sockets from /tmp into the runtime directory; both are worth trying.
        foreach (var root in new[] { Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"), "/tmp" })
        {
            if (string.IsNullOrEmpty(root))
                continue;

            var socket = Path.Combine(root, "hypr", signature, SocketName);
            if (File.Exists(socket))
                return socket;
        }

        Log.Debug("Hyprland is running but its command socket was not found");
        return null;
    }

    /// <summary>Runs a dispatch for one window, turning every way it can fail into a plain false.</summary>
    private static bool Run(string title, Func<(string Socket, string Selector), bool> action) =>
        Try.Run(() =>
        {
            if (CommandSocket.Value is not { } socket)
                return false;

            return action((socket, $"title:^{Regex.Escape(title)}$"));
        }).GetOrElse(ex =>
        {
            Log.Debug(ex, "Hyprland could not be asked about the window {Title}", title);
            return false;
        });

    /// <summary>Sends one command and waits for Hyprland to answer <c>ok</c>; the socket closes after it.</summary>
    private static bool Dispatch(string socketPath, string command)
    {
        var answer = Ask(socketPath, $"dispatch {command}");
        if (answer is "ok")
            return true;

        if (answer is not null)
            Log.Debug("Hyprland answered {Answer} to {Command}", answer, command);

        return false;
    }

    /// <summary>Sends one command and returns the answer, or null when Hyprland could not be reached.</summary>
    private static string? Ask(string socketPath, string command) =>
        Try.Run<string?>(() =>
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(socketPath));
            socket.SendTimeout = 500;
            socket.ReceiveTimeout = 500;

            socket.Send(Encoding.UTF8.GetBytes(command));

            var reply = new MemoryStream();
            Span<byte> buffer = stackalloc byte[4096];

            int read;
            while ((read = socket.Receive(buffer)) > 0)
                reply.Write(buffer[..read]);

            return Encoding.UTF8.GetString(reply.GetBuffer(), 0, (int)reply.Length).Trim();
        }).GetOrElse(ex =>
        {
            Log.Debug(ex, "Hyprland did not answer on {Socket}", socketPath);
            return null;
        });

    /// <summary>The point Hyprland reports as <c>{"x": ..., "y": ...}</c>; null when it is not one.</summary>
    private static (int X, int Y)? Point(string? json)
    {
        if (json is null)
            return null;

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        return (root.GetProperty("x").GetInt32(), root.GetProperty("y").GetInt32());
    }

    /// <summary>
    ///     Keeps a <paramref name="size" />-pixel square inside the monitor the pointer is on. Hyprland
    ///     moves exactly as asked, so a window placed next to a pointer at the edge of the screen would
    ///     otherwise hang off it.
    /// </summary>
    private static (int X, int Y) ClampToMonitor(string? json, (int X, int Y) cursor, (int X, int Y) point, int size)
    {
        if (json is null)
            return point;

        using var document = JsonDocument.Parse(json);
        foreach (var monitor in document.RootElement.EnumerateArray())
        {
            var left = monitor.GetProperty("x").GetInt32();
            var top = monitor.GetProperty("y").GetInt32();
            var scale = monitor.GetProperty("scale").GetDouble();

            // Hyprland reports a monitor's size in pixels and its position in layout coordinates.
            var width = (int)(monitor.GetProperty("width").GetInt32() / scale);
            var height = (int)(monitor.GetProperty("height").GetInt32() / scale);

            if (cursor.X < left || cursor.X >= left + width || cursor.Y < top || cursor.Y >= top + height)
                continue;

            return (Math.Clamp(point.X, left, left + width - size), Math.Clamp(point.Y, top, top + height - size));
        }

        return point;
    }
}
