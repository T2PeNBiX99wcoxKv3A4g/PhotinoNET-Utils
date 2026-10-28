using System.IO.Pipes;
using JetBrains.Annotations;
using Serilog;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Utils;

namespace PhotinoNetUtils.Services;

/// <summary>
///     Ensures a single running instance. Later launches forward their command-line arguments to the
///     first instance over a named pipe and then exit.
/// </summary>
/// <remarks>
///     The first-instance check is an exclusively locked file, not a named mutex: file locks are
///     enforced across processes by both platforms (share modes on Windows, <c>flock</c> on Unix),
///     whereas a named <see cref="Mutex" /> is not a reliable cross-process primitive on Unix.
/// </remarks>
public sealed class SingleInstance : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly FileStream? _lockFile;
    private readonly string _pipeName;

    public SingleInstance(string appName)
    {
        _pipeName = $"{appName}.Ipc";

        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            appName, "instance.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            _lockFile = new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            IsFirstInstance = true;
        }
        catch (IOException)
        {
            // Another instance already holds the lock.
            IsFirstInstance = false;
        }

        Log.Information("Single instance: {IsFirst} (lock {Path})", IsFirstInstance, path);
    }

    public bool IsFirstInstance { get; }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _lockFile?.Dispose();
    }

    /// <summary>Hands the arguments to the already-running instance. Returns false on failure.</summary>
    [PublicAPI]
    public async Task<bool> ForwardAsync(IReadOnlyList<string> args) =>
        (await Try.Run(async () =>
        {
            await using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            await client.ConnectAsync(3000);

            await using var writer = new StreamWriter(client);
            await writer.WriteAsync(string.Join('\n', args));
            await writer.FlushAsync();

            Log.Information("Forwarded {Count} argument(s) to the running instance", args.Count);
            return true;
        })).GetOrElse(ex =>
        {
            Log.Warning(ex, "Failed to forward arguments to the running instance");
            return false;
        });

    /// <summary>Starts the pipe server; <paramref name="onArguments" /> runs on a background thread.</summary>
    public void StartServer(Action<IReadOnlyList<string>> onArguments) =>
        _ = Task.Run(() => ServerLoopAsync(onArguments));

    private async Task ServerLoopAsync(Action<IReadOnlyList<string>> onArguments)
    {
        while (!_cts.IsCancellationRequested)
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(_cts.Token);

                using var reader = new StreamReader(server);
                var payload = await reader.ReadToEndAsync(_cts.Token);

                var args = payload.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (args.Length > 0)
                    onArguments(args);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "IPC server error");

                try
                {
                    await Task.Delay(250, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
    }
}
