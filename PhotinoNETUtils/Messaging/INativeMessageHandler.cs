using JetBrains.Annotations;

namespace PhotinoNetUtils.Messaging;

/// <summary>
///     Answers the native messages one service owns. The <c>[NativeMessage]</c> generator turns the
///     annotated methods into <see cref="HandleAsync" />, so a service never spells the dispatch
///     switch, and <c>NativeBridge.Subscribe(this)</c> connects it.
/// </summary>
[PublicAPI]
public interface INativeMessageHandler
{
    /// <summary>
    ///     Handles one request, or returns <c>null</c> when the type belongs to another handler.
    /// </summary>
    Task<NativeResponse?> HandleAsync(NativeContext context);
}