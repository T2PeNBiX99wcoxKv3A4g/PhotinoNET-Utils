using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using JetBrains.Annotations;
using Photino.NET;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Utils;
using Serilog;

namespace PhotinoNetUtils.Messaging;

/// <summary>
///     The transport for JSON messages exchanged with the React front-end: it parses an incoming
///     envelope, raises <see cref="MessageReceived" /> and posts the first reply back. The handlers
///     live with the services that own each message type.
/// </summary>
/// <remarks>
///     Every message is handled on a thread-pool thread, so a slow handler (listing a large folder,
///     measuring a tree, asking the shell for an icon) can never freeze the window. The reply is
///     posted with <see cref="PhotinoWindow.Invoke" />, the only part that has to run on the UI thread.
/// </remarks>
[PublicAPI]
public static class NativeBridge
{
    /// <summary>
    ///     Raised for every incoming message. Handlers are awaited in subscription order and the first
    ///     non-null response wins.
    /// </summary>
    public static event NativeMessageHandler? MessageReceived;

    /// <summary>
    ///     Supplies the app's source-generated <see cref="JsonSerializerContext" />. Call once, before
    ///     the interface can send anything: the bridge never reflects over payload types.
    /// </summary>
    public static void Initialize(JsonSerializerContext context) => NativeJson.Use(context);

    public static Task HandleMessageAsync(PhotinoWindow? window, string rawMessage) =>
        Task.Run(() => HandleCoreAsync(window, rawMessage));

    /// <summary>Serializes an unsolicited push for <c>window.SendWebMessage(...)</c>.</summary>
    public static string SerializeEvent(string type, object? data = null) =>
        JsonSerializer.Serialize(
            new()
            {
                Type = type,
                Data = NativeJson.ToElement(data)
            },
            NativeJson.TypeInfo<NativeEvent>());

    private static async Task HandleCoreAsync(PhotinoWindow? window, string rawMessage)
    {
        Log.Debug("<< {Message}", rawMessage.RemoveControlChars().Truncate(600));

        NativeRequest? request;
        try
        {
            request = JsonSerializer.Deserialize(rawMessage, NativeJson.TypeInfo<NativeRequest>());
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "Invalid JSON received");
            Send(window, NativeResponse.Failure(null, "error", "error.invalidJson"));
            return;
        }

        if (request?.Type is null or "")
            return;

        var context = new NativeContext(window, request);

        (await Try.Run(async () =>
        {
            var response = await RaiseAsync(context);

            if (response is not null)
                Send(window, response);
            else if (context.WantsReply)
                Send(window, context.Fail($"{request.Type}.result", "error.unsupported"));
        })).OnFailure(ex =>
        {
            Log.Error(ex, "Handler for {Type} failed", request.Type);

            if (context.WantsReply)
                Send(window, context.Fail($"{request.Type}.result", ex.Message));
        });
    }

    private static async Task<NativeResponse?> RaiseAsync(NativeContext context)
    {
        if (MessageReceived is not { } handlers)
            return null;

        foreach (var handler in handlers.GetInvocationList().Cast<NativeMessageHandler>())
            if (await handler(context) is { } response)
                return response;

        return null;
    }

    private static void Send(PhotinoWindow? window, NativeResponse response)
    {
        var json = NativeJson.Serialize(response);
        Log.Debug(">> {Message}", json.RemoveControlChars().Truncate(600));

        if (window is null)
            return;

        Try.Run(() => window.Invoke(() => window.SendWebMessage(json)))
            .OnFailure(ex => Log.Warning(ex, "Could not send the reply"));
    }
}

/// <summary>
///     The app's serializer context, plus the conversions the envelope types need. Kept internal: a
///     host only ever passes its context to <see cref="NativeBridge.Initialize" />.
/// </summary>
internal static class NativeJson
{
    private static JsonSerializerContext? _context;

    public static void Use(JsonSerializerContext context) => _context = context;

    /// <summary>Converts a registered payload into the <see cref="JsonElement" /> the envelope embeds.</summary>
    public static JsonElement? ToElement(object? value) =>
        value is null ? null : JsonSerializer.SerializeToElement(value, TypeInfo(value.GetType()));

    /// <summary>Resolves the type info for a registered type, or throws when it was never registered.</summary>
    public static JsonTypeInfo TypeInfo(Type type) =>
        _context?.GetTypeInfo(type) ?? throw NotRegistered(type);

    /// <summary>Typed overload of <see cref="TypeInfo(Type)" />, for deserializing a payload.</summary>
    public static JsonTypeInfo<T> TypeInfo<T>() =>
        _context?.GetTypeInfo(typeof(T)) as JsonTypeInfo<T> ?? throw NotRegistered(typeof(T));

    public static string Serialize(NativeResponse response) =>
        JsonSerializer.Serialize(response, TypeInfo<NativeResponse>());

    private static InvalidOperationException NotRegistered(Type type) =>
        _context is null
            ? new(
                $"Call {nameof(NativeBridge)}.{nameof(NativeBridge.Initialize)} with the app's " +
                "JsonSerializerContext before exchanging messages.")
            : new InvalidOperationException(
                $"JSON type '{type}' is not registered in {_context.GetType().Name}.");
}