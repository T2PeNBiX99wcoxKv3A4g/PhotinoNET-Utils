using JetBrains.Annotations;

namespace PhotinoNetUtils.Messaging;

/// <summary>Body for the <c>window.state</c> event.</summary>
[PublicAPI]
public sealed record WindowStatePayload(bool Maximized);

/// <summary>Payload for <c>window.resize</c>: the edge being dragged.</summary>
[PublicAPI]
public sealed record WindowResizeRequest(string Direction);