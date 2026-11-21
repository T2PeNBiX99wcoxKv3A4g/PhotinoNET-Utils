using JetBrains.Annotations;

namespace PhotinoNetUtils.Notifications;

/// <summary>
///     Extra content for a notification: what a click should carry, and the optional pieces the
///     platform supports on top of a title and a message.
/// </summary>
[PublicAPI]
public sealed record NotificationOptions
{
    /// <summary>Handed back through <see cref="NotificationService.Activated" /> when it is clicked.</summary>
    public string? Action { get; init; }

    /// <summary>Absolute path of an image; Windows shows it as a round app logo, Linux as the icon.</summary>
    public string? ImagePath { get; init; }

    /// <summary>Small text under the message, usually the app name. Windows only.</summary>
    public string? Attribution { get; init; }

    /// <summary>Label for an extra button that activates the same action. Windows only.</summary>
    public string? ButtonText { get; init; }
}