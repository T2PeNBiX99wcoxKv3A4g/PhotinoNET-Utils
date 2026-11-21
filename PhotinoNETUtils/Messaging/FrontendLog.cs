using System.Text.Json;
using JetBrains.Annotations;
using Serilog;
using Serilog.Events;

namespace PhotinoNetUtils.Messaging;

/// <summary>
///     Receives the web view's <c>log.write</c> messages and writes them into the same Serilog
///     pipeline as the host. The page can log through the bridge without knowing how the file or
///     console sink is configured.
/// </summary>
[PublicAPI]
public static class FrontendLog
{
    private const int MaxMessageLength = 16 * 1024;
    private const int MaxContextLength = 8 * 1024;

    private static readonly Lock Sync = new();
    private static bool _attached;

    /// <summary>Starts listening for front-end log messages. Called by <see cref="Logging.Initialize" />.</summary>
    public static void Attach()
    {
        lock (Sync)
        {
            if (_attached)
                return;

            _attached = true;
            NativeBridge.MessageReceived += HandleMessageAsync;
        }
    }

    private static Task<NativeResponse?> HandleMessageAsync(NativeContext context)
    {
        if (context.Request.Type != "log.write" ||
            context.Request.Payload is not { ValueKind: JsonValueKind.Object } payload)
            return Task.FromResult<NativeResponse?>(null);

        var level = Read(payload, "level").ToLowerInvariant() switch
        {
            "trace" or "verbose" => LogEventLevel.Verbose,
            "debug" => LogEventLevel.Debug,
            "warn" or "warning" => LogEventLevel.Warning,
            "error" => LogEventLevel.Error,
            "fatal" or "critical" => LogEventLevel.Fatal,
            _ => LogEventLevel.Information
        };

        var message = Limit(Read(payload, "message"), MaxMessageLength);
        var source = Read(payload, "source");
        source = source.Length > 0 ? source : "frontend";
        var data = payload.TryGetProperty("context", out var extra) ? extra.GetRawText() : "";

        if (message.Length == 0 && data.Length == 0)
            return Task.FromResult<NativeResponse?>(null);

        Log.Write(level, "[{Source}] {Message}{Context}", source, message,
            data.Length == 0 ? "" : " " + Limit(data, MaxContextLength));

        // A log message never expects a reply; the front end uses Native.send, not request.
        return Task.FromResult<NativeResponse?>(null);
    }

    private static string Read(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static string Limit(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum] + "…";
}
