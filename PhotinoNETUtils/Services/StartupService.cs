using JetBrains.Annotations;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Messaging;
using PhotinoNetUtils.Platform;
using PhotinoNetUtils.Utils;
using Serilog;

namespace PhotinoNetUtils.Services;

/// <summary>Body for the <c>app.startup</c> messages; the entry's presence is the state.</summary>
[PublicAPI]
public sealed record StartupPayload(bool Supported, bool Enabled);

/// <summary>Payload for <c>app.setStartup</c>: whether the startup entry should exist.</summary>
[PublicAPI]
public sealed record StartupRequest(bool Enabled);

/// <summary>
///     Answers the <c>app.startup</c> messages and keeps an existing autostart entry in step.
/// </summary>
/// <remarks>
///     The entry's presence is the state, so deleting it by hand turns the feature off too. The
///     platform-specific part lives in <see cref="StartupRegistration" />; this class only adds the
///     bridge messages and the refresh a moved executable needs.
/// </remarks>
[PublicAPI]
public sealed partial class StartupService : IDisposable
{
    private readonly StartupRegistration _registration;
    private readonly IDisposable _subscription;

    public StartupService(StartupRegistration registration)
    {
        _registration = registration;
        _subscription = NativeBridge.Subscribe(this);
        Refresh();
    }

    /// <summary>True on platforms with a per-user autostart location.</summary>
    public bool IsSupported => StartupRegistration.IsSupported;

    /// <summary>Whether the autostart entry currently exists.</summary>
    public bool IsEnabled => _registration.IsEnabled;

    public void Dispose() => _subscription.Dispose();

    /// <summary>Rewrites an existing entry so a moved executable or a new icon is picked up.</summary>
    private void Refresh()
    {
        if (IsEnabled)
            Try.Run(_registration.Refresh)
                .OnFailure(ex => Log.Warning(ex, "Failed to refresh the autostart entry"));
    }

    [NativeMessage("app.startup")]
    private NativeResponse Status(NativeContext context) =>
        context.Reply("app.startup.result", new StartupPayload(IsSupported, IsEnabled));

    [NativeMessage("app.setStartup")]
    private NativeResponse Set(NativeContext context)
    {
        var enabled = context.Payload<StartupRequest>()?.Enabled ?? false;

        if (!IsSupported)
            return context.Reply("app.setStartup.result", new StartupPayload(false, false));

        Try.Run(() => _registration.SetEnabled(enabled))
            .OnFailure(ex => Log.Warning(ex, "Failed to update the autostart entry"));

        return context.Reply("app.setStartup.result", new StartupPayload(IsSupported, IsEnabled));
    }
}