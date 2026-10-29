using Serilog;

namespace PhotinoNetUtils.Interop;

/// <summary>
///     Stand-in tray icon for platforms that have none, so the app keeps its main window visible
///     instead of hiding with no way back.
/// </summary>
internal sealed class NullTrayIcon : ITrayIcon
{
    public bool Start()
    {
        Log.Information("No tray icon on this platform; the main window stays visible");
        return false;
    }

    public void Dispose()
    {
    }
}