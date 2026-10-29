using System.Text;
using JetBrains.Annotations;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace PhotinoNetUtils;

/// <summary>Configures the Serilog pipeline used across a Photino app.</summary>
public static class Logging
{
    public const string ConsoleTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";

    /// <summary>
    ///     Writes to the console and to <c>%AppData%/{appName}/logs/{prefix}-.log</c>, and mirrors
    ///     everything the host and native libraries print into the same file.
    /// </summary>
    [PublicAPI]
    public static void Initialize(string appName, string? logFilePrefix = null)
    {
        var prefix = string.IsNullOrWhiteSpace(logFilePrefix) ? appName.ToLowerInvariant() : logFilePrefix;
        var logDirectory = AppPaths.LogDirectory(appName);

        // Keep the real console before anything replaces it: the sink has to write there directly, or the
        // capture installed below would feed its own output straight back in.
        var console = Console.Out;

        Log.Logger = new LoggerConfiguration()
#if DEBUG
            .MinimumLevel.Is(LogEventLevel.Debug)
#else
            .MinimumLevel.Is(LogEventLevel.Information)
#endif
            .Enrich.FromLogContext()
            .Enrich.WithProperty("SourceContext", "Core")
            .WriteTo.Sink(new ConsoleSink(console))
            .WriteTo.File(
                Path.Combine(logDirectory, $"{prefix}-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                shared: true,
                outputTemplate:
                "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        // The host, the toolkit and native libraries all print to the console. Mirroring that into the log
        // means the file can be sent on its own, with nothing to copy by hand.
        Console.SetOut(new TeeWriter(console, "out", LogEventLevel.Information));
        Console.SetError(new TeeWriter(Console.Error, "err", LogEventLevel.Warning));

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "Unhandled exception (terminating: {Terminating})",
                args.IsTerminating);

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };
    }

    public static void Shutdown() => Log.CloseAndFlush();
}

/// <summary>
///     Writes log events to the real console, bypassing <see cref="TeeWriter" /> — otherwise a mirrored
///     console line would be logged, printed, mirrored again, and so on.
/// </summary>
internal sealed class ConsoleSink(TextWriter writer) : ILogEventSink
{
    private readonly MessageTemplateTextFormatter _formatter = new(Logging.ConsoleTemplate);
    private readonly Lock _sync = new();

    public void Emit(LogEvent logEvent)
    {
        lock (_sync)
        {
            _formatter.Format(logEvent, writer);
            writer.Flush();
        }
    }
}

/// <summary>
///     Passes everything through to the real console and also logs it, one line at a time so partial
///     writes do not end up as fragments in the log.
/// </summary>
internal sealed class TeeWriter(TextWriter inner, string source, LogEventLevel level) : TextWriter
{
    private readonly StringBuilder _pending = new();
    private readonly Lock _sync = new();

    public override Encoding Encoding => inner.Encoding;

    public override void Write(char value)
    {
        inner.Write(value);

        lock (_sync)
            if (value == '\n')
                EmitPending();
            else if (value != '\r')
                _pending.Append(value);
    }

    public override void Write(string? value)
    {
        inner.Write(value);
        if (value is null)
            return;

        lock (_sync)
            foreach (var character in value)
                if (character == '\n')
                    EmitPending();
                else if (character != '\r')
                    _pending.Append(character);
    }

    public override void WriteLine(string? value) => Write(value + Environment.NewLine);

    private void EmitPending()
    {
        var line = _pending.ToString().TrimEnd();
        _pending.Clear();

        if (line.Length > 0)
            Log.Write(level, "[{Source}] {Line}", source, line);
    }
}