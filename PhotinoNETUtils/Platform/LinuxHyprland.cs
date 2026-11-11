using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Utils;
using Serilog;

namespace PhotinoNetUtils.Platform;

/// <summary>
///     Window geometry through Hyprland's command socket, for the one thing Wayland does not let a
///     client do itself: change the size of its own window.
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
    public static bool Resize(string title, int width, int height)
    {
        if (CommandSocket.Value is not { } socket)
            return false;

        var window = $"title:^{Regex.Escape(title)}$";

        return Send(socket,
                   $"hl.dsp.window.resize({{ window = \"{window}\", x = {width}, y = {height}, relative = false }})") &&
               Send(socket, $"hl.dsp.window.center({{ window = \"{window}\" }})");
    }

    private static string? FindCommandSocket()
    {
        if (Environment.GetEnvironmentVariable(InstanceVariable) is not { Length: > 0 } signature)
            return null;

        // Hyprland moved its sockets from /tmp into the runtime directory; both are worth trying.
        foreach (var root in new[]
                 {
                     Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"), "/tmp"
                 })
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

    /// <summary>Sends one command and waits for Hyprland to answer <c>ok</c>; the socket closes after it.</summary>
    private static bool Send(string socketPath, string command) =>
        Try.Run(() =>
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(socketPath));
            socket.SendTimeout = 500;
            socket.ReceiveTimeout = 500;

            socket.Send(Encoding.UTF8.GetBytes($"dispatch {command}"));

            var reply = new MemoryStream();
            Span<byte> buffer = stackalloc byte[128];

            int read;
            while ((read = socket.Receive(buffer)) > 0)
                reply.Write(buffer[..read]);

            var answer = Encoding.UTF8.GetString(reply.GetBuffer(), 0, (int)reply.Length).Trim();
            if (answer is "ok")
                return true;

            Log.Debug("Hyprland answered {Answer} to {Command}", answer, command);
            return false;
        }).GetOrElse(ex =>
        {
            Log.Debug(ex, "Hyprland did not answer on {Socket}", socketPath);
            return false;
        });
}