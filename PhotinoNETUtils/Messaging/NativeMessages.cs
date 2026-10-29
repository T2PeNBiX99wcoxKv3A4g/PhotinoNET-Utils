using System.Text.Json;
using JetBrains.Annotations;
using Photino.NET;

namespace PhotinoNetUtils.Messaging;

/// <summary>
///     A message sent from the React front-end through <c>window.external.sendMessage(...)</c>.
/// </summary>
[PublicAPI]
public sealed class NativeRequest
{
    /// <summary>Logical message name, e.g. <c>fs.list</c>.</summary>
    public string? Type { get; set; }

    /// <summary>Optional correlation id; when present the handler replies with the same id.</summary>
    public string? Id { get; set; }

    /// <summary>Optional message body, kept as a raw element until the handler knows what to expect.</summary>
    public JsonElement? Payload { get; set; }
}

/// <summary>
///     A reply sent back to the front-end through <c>window.external.receiveMessage(...)</c>.
/// </summary>
[PublicAPI]
public sealed class NativeResponse
{
    public string? Id { get; init; }
    public string Type { get; init; } = string.Empty;
    public bool Ok { get; init; }

    /// <summary>The already-serialized body, so the envelope stays free of reflection.</summary>
    public JsonElement? Data { get; init; }

    public string? Error { get; init; }

    public static NativeResponse Success(string? id, string type, object? data = null) =>
        new()
        {
            Id = id,
            Type = type,
            Ok = true,
            Data = NativeJson.ToElement(data)
        };

    public static NativeResponse Failure(string? id, string type, string error) =>
        new()
        {
            Id = id,
            Type = type,
            Ok = false,
            Error = error
        };
}

/// <summary>
///     An unsolicited push from C# to the front-end (no request id, no reply expected).
/// </summary>
[PublicAPI]
public sealed class NativeEvent
{
    public string Type { get; init; } = string.Empty;
    public JsonElement? Data { get; init; }
}

/// <summary>Everything a handler needs to answer one incoming message.</summary>
[PublicAPI]
public sealed record NativeContext(PhotinoWindow? Window, NativeRequest Request)
{
    public string? Id => Request.Id;

    /// <summary>True when the sender supplied an id and therefore expects a reply.</summary>
    public bool WantsReply => Request.Id is { Length: > 0 };

    public T? Payload<T>() =>
        Request.Payload is { } payload ? payload.Deserialize(NativeJson.TypeInfo<T>()) : default;

    public NativeResponse Reply(string type, object? data = null) => NativeResponse.Success(Id, type, data);

    public NativeResponse Fail(string type, string error) => NativeResponse.Failure(Id, type, error);
}

/// <summary>
///     Handles a subset of message types. Returns a response, or <c>null</c> to decline the message
///     (either it belongs to another handler, or it needs no reply).
/// </summary>
[PublicAPI]
public delegate Task<NativeResponse?> NativeMessageHandler(NativeContext context);