using System.Runtime.InteropServices;
using JetBrains.Annotations;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Utils;
using Serilog;

namespace PhotinoNetUtils.Platform;

/// <summary>
///     Routes termination signals to the application's own shutdown path instead of letting the
///     runtime wait for the blocked Photino message loop.
/// </summary>
/// <remarks>
///     A GUI application keeps its main thread inside Photino's native loop. The runtime catches
///     SIGTERM, but without a handler it has no way to close that loop, so logout or a service stop
///     would wait until the system forces the process down. The first signal asks the app to close;
///     a second one forces the process to exit if that close is stuck.
/// </remarks>
[PublicAPI]
public static class TerminationSignals
{
    private static int _requested;

    /// <summary>
    ///     Starts listening for SIGTERM, SIGINT and SIGHUP. Returns null on platforms where the
    ///     desktop shell already owns process termination (Windows).
    /// </summary>
    public static IDisposable? Handle(Action shutdown)
    {
        if (!OperatingSystem.IsLinux())
            return null;

        PosixSignalRegistration[] registrations =
        [
            Register(PosixSignal.SIGTERM, shutdown),
            Register(PosixSignal.SIGINT, shutdown),
            Register(PosixSignal.SIGHUP, shutdown)
        ];

        return new Registrations(registrations);
    }

    private static PosixSignalRegistration Register(PosixSignal signal, Action shutdown) =>
        PosixSignalRegistration.Create(signal, context =>
        {
            context.Cancel = true;
            Log.Information("Received {Signal}; asking the application to close", signal);

            if (Interlocked.Exchange(ref _requested, 1) == 0)
                Try.Run(shutdown).OnFailure(ex => Log.Warning(ex, "Shutdown from {Signal} failed", signal));
            else
                Environment.Exit(128 + (int)signal);
        });

    private sealed class Registrations(PosixSignalRegistration[] registrations) : IDisposable
    {
        public void Dispose()
        {
            foreach (var registration in registrations)
                registration.Dispose();
        }
    }
}