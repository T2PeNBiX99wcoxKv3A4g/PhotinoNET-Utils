using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Utils;
using Serilog;

namespace PhotinoNetUtils.Interop;

/// <summary>
///     Runs a tray callback. The native layer invokes these from inside its own message loop, so a
///     failure has to be logged instead of being allowed to escape into it.
/// </summary>
internal static class TrayAction
{
    public static void Invoke(Action action) =>
        Try.Run(action).OnFailure(ex => Log.Warning(ex, "Tray callback failed"));
}
