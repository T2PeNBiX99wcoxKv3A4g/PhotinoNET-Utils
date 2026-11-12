using JetBrains.Annotations;

namespace PhotinoNetUtils.Messaging;

/// <summary>
///     A service that answers native messages without any wiring: the constructor subscribes the
///     generated <see cref="INativeMessageHandler.HandleAsync" /> and <see cref="Dispose" /> removes
///     it again. Derive from this class when the service has no other base class; a class that
///     already has one can call <see cref="NativeBridge.Subscribe" /> instead.
/// </summary>
[PublicAPI]
public abstract class NativeMessageService : INativeMessageHandler, IDisposable
{
    private readonly IDisposable _subscription;

    protected NativeMessageService() => _subscription = NativeBridge.Subscribe(this);

    public void Dispose() => _subscription.Dispose();

    /// <summary>The generated dispatcher; the default answers nothing when there is no handler.</summary>
    public virtual Task<NativeResponse?> HandleAsync(NativeContext context) =>
        Task.FromResult<NativeResponse?>(null);
}
