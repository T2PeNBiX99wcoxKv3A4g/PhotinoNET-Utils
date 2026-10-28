using JetBrains.Annotations;

namespace PhotinoNetUtils.Interop;

/// <summary>A notification-area (tray) icon. Only implemented where the platform provides one.</summary>
[PublicAPI]
public interface ITrayIcon : IDisposable
{
    /// <summary>
    ///     Shows the icon and reports whether it was actually created. Implementations may have to defer
    ///     the work, so a <c>false</c> result only means "not available", never "still starting".
    /// </summary>
    bool Start();
}

/// <summary>
///     One entry of a tray context menu. The separator belongs before the item, which keeps the
///     declaration order the same as the rendered order.
/// </summary>
[PublicAPI]
public sealed record TrayMenuItem(string Label, Action Callback, bool SeparatorBefore = false);
