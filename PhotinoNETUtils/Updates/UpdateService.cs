using System.Diagnostics;
using JetBrains.Annotations;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Messaging;
using PhotinoNetUtils.Platform;
using PhotinoNetUtils.Utils;
using Serilog;

namespace PhotinoNetUtils.Updates;

/// <summary>The repository and asset shape one application uses for its releases.</summary>
[PublicAPI]
public sealed record UpdateOptions(
    string Owner,
    string Repository,
    string AppName,
    string CurrentVersion,
    string AssetPattern,
    string ReleasePageUrl,
    string LogDirectory,
    Action? Shutdown = null,
    Action<string, object?>? Notify = null,
    string Branch = "master",
    string ChangelogPath = "CHANGELOG.md");

/// <summary>Body for <c>update.status</c> and <c>update.check</c>.</summary>
[PublicAPI]
public sealed record UpdateCheckPayload(string Current, string Latest, bool Available);

/// <summary>Body for an update action that only needs to say whether it started.</summary>
[PublicAPI]
public sealed record UpdateAckPayload(bool Ok, string? Error = null);

/// <summary>
///     One <c>update.progress</c> event. <see cref="Phase" /> is <c>changelog</c>,
///     <c>downloading</c>, <c>ready</c>, <c>cancelled</c> or <c>error</c>.
/// </summary>
[PublicAPI]
public sealed record UpdateProgressPayload(
    string Phase,
    long Downloaded,
    long Total,
    int Percent,
    string? Error = null);

/// <summary>Body for <c>update.changelog</c>, fetched from the repository's markdown file.</summary>
[PublicAPI]
public sealed record UpdateChangelogPayload(string Text);

/// <summary>
///     Shared check, download and install flow for a GitHub release. It deliberately uses the raw
///     version file and the release download redirect instead of the GitHub API, so checking and
///     downloading are not limited by the API's anonymous rate limit.
/// </summary>
[PublicAPI]
public sealed class UpdateService : IDisposable
{
    private const string WindowsOnlyError = "Updates are only supported on Windows.";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6);
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ChangelogTimeout = TimeSpan.FromSeconds(20);
    private static readonly ILogger Logger = Log.ForContext<UpdateService>();

    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private readonly HttpClient _client;
    private readonly Lock _gate = new();
    private readonly UpdateOptions _options;
    private UpdateCheckPayload? _cached;
    private DateTime _cachedAt;
    private CancellationTokenSource? _download;
    private bool _downloading;
    private string? _readyPath;

    public UpdateService(UpdateOptions options)
    {
        _options = options;
        _client = new()
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        _client.DefaultRequestHeaders.UserAgent.Add(
            new(options.AppName, options.CurrentVersion));

        Cleanup();
        NativeBridge.MessageReceived += HandleMessageAsync;
    }

    /// <summary>Where a download is staged: one folder per app under the temp directory.</summary>
    private string DownloadDirectory => Path.Combine(Path.GetTempPath(), $"{_options.AppName}-update");

    public void Dispose()
    {
        NativeBridge.MessageReceived -= HandleMessageAsync;
        _download?.Cancel();
        _client.Dispose();
    }

    private Task<NativeResponse?> HandleMessageAsync(NativeContext context) => context.Request.Type switch
    {
        "update.status" => StatusAsync(context),
        "update.check" => CheckAsync(context),
        "update.download" => DownloadAsync(context),
        "update.cancel" => Task.FromResult<NativeResponse?>(Cancel(context)),
        "update.install" => Task.FromResult<NativeResponse?>(Install(context)),
        "update.openLogs" => Task.FromResult<NativeResponse?>(
            Open(context, _options.LogDirectory, "update.openLogs.result")),
        "update.openRelease" => Task.FromResult<NativeResponse?>(
            Open(context, _options.ReleasePageUrl, "update.openRelease.result")),
        _ => Task.FromResult<NativeResponse?>(null)
    };

    /// <summary>The automatic check: answers from the cache and stays silent when it cannot read.</summary>
    private async Task<NativeResponse?> StatusAsync(NativeContext context) =>
        context.Reply("update.status.result", await ResolveAsync(false));

    /// <summary>The check the user asked for: always reads the version file and reports a failure.</summary>
    private async Task<NativeResponse?> CheckAsync(NativeContext context) =>
        await ResolveAsync(true) is { } check
            ? context.Reply("update.check.result", check)
            : context.Fail("update.check.result",
                Localization.Localization.Get("error.updateCheckFailed"));

    private async Task<UpdateCheckPayload?> ResolveAsync(bool force)
    {
        await _checkGate.WaitAsync();
        try
        {
            if (!force && _cached is { } cached && DateTime.UtcNow - _cachedAt < CacheLifetime)
                return cached;

            var published = await Try.Run(ReadPublishedVersionAsync);

            if (published.Exception is { } exception)
            {
                Logger.Warning(exception, "Failed to check for a newer version");
                return null;
            }

            if (published.Value is not { Length: > 0 } latest)
                return null;

            var check = new UpdateCheckPayload(_options.CurrentVersion, latest,
                IsNewer(latest, _options.CurrentVersion));

            _cached = check;
            _cachedAt = DateTime.UtcNow;
            _options.Notify?.Invoke("update.checked", check);
            return check;
        }
        finally
        {
            _checkGate.Release();
        }
    }

    private async Task<string> ReadPublishedVersionAsync()
    {
        using var timeout = new CancellationTokenSource(CheckTimeout);
        var url = $"https://raw.githubusercontent.com/{_options.Owner}/{_options.Repository}/{_options.Branch}/.version";
        using var response = await _client.GetAsync(url, timeout.Token);
        response.EnsureSuccessStatusCode();

        var text = await response.Content.ReadAsStringAsync(timeout.Token);
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "";

        return line.TrimStart('v', 'V');
    }

    /// <summary>Starts the download in the background; progress arrives as events.</summary>
    private async Task<NativeResponse?> DownloadAsync(NativeContext context)
    {
        if (!OperatingSystem.IsWindows())
            return Ack(context, "update.download.result", false, WindowsOnlyError);

        lock (_gate)
            if (_downloading)
                return Ack(context, "update.download.result", false, "A download is already running.");

        var check = _cached ?? await ResolveAsync(true);
        if (check is not { Available: true })
            return Ack(context, "update.download.result", false, "No update is available.");

        CancellationToken token;

        lock (_gate)
        {
            if (_downloading)
                return Ack(context, "update.download.result", false, "A download is already running.");

            _downloading = true;
            _readyPath = null;
            _download = new();
            token = _download.Token;
        }

        _ = Task.Run(() => DownloadCoreAsync(check.Latest, token), token);
        return Ack(context, "update.download.result", true, null);
    }

    private async Task DownloadCoreAsync(string version, CancellationToken token)
    {
        var part = "";

        try
        {
            Notify("update.progress", new UpdateProgressPayload("changelog", 0, 0, 0));
            var changelog = (await Try.Run(() => ReadChangelogAsync(token))).GetOrNull();
            if (changelog is { Length: > 0 })
                Notify("update.changelog", new UpdateChangelogPayload(changelog));

            var versionText = version.TrimStart('v', 'V');
            var fileName = _options.AssetPattern.Replace("{version}", versionText);
            var url =
                $"https://github.com/{_options.Owner}/{_options.Repository}/releases/latest/download/{Uri.EscapeDataString(fileName)}";

            ClearDownloadDirectory();

            var path = Path.Combine(DownloadDirectory, fileName);
            part = path + ".part";

            using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? 0;
            await using var source = await response.Content.ReadAsStreamAsync(token);

            long downloaded = 0;
            var lastPercent = -2;
            var lastTick = 0L;

            await using (var target = File.Create(part))
            {
                var buffer = new byte[81920];
                int read;

                while ((read = await source.ReadAsync(buffer, token)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), token);
                    downloaded += read;

                    var percent = total > 0
                        ? (int)Math.Min(100, downloaded * 100 / total)
                        : -1;
                    var now = Environment.TickCount64;

                    if (percent == lastPercent || now - lastTick < 150 && percent != 100)
                        continue;

                    Notify("update.progress",
                        new UpdateProgressPayload("downloading", downloaded, total, percent));
                    lastPercent = percent;
                    lastTick = now;
                }

                await target.FlushAsync(token);
            }

            File.Move(part, path, true);

            lock (_gate)
                _readyPath = path;

            Notify("update.progress", new UpdateProgressPayload("ready", downloaded, total, 100));
        }
        catch (OperationCanceledException)
        {
            Notify("update.progress", new UpdateProgressPayload("cancelled", 0, 0, 0));
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Update download failed");
            Notify("update.progress", new UpdateProgressPayload("error", 0, 0, 0, ex.Message));
        }
        finally
        {
            if (part.Length > 0)
                Try.Run(() => File.Delete(part));

            lock (_gate)
            {
                _downloading = false;
                _download?.Dispose();
                _download = null;
            }
        }
    }

    private async Task<string?> ReadChangelogAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(ChangelogTimeout);

        var url =
            $"https://raw.githubusercontent.com/{_options.Owner}/{_options.Repository}/{_options.Branch}/{_options.ChangelogPath}";
        using var response = await _client.GetAsync(url, timeout.Token);

        return response.IsSuccessStatusCode
            ? await response.Content.ReadAsStringAsync(timeout.Token)
            : null;
    }

    private NativeResponse Cancel(NativeContext context)
    {
        lock (_gate)
            _download?.Cancel();

        return Ack(context, "update.cancel.result", true, null);
    }

    private NativeResponse Install(NativeContext context)
    {
        if (!OperatingSystem.IsWindows())
            return Ack(context, "update.install.result", false, WindowsOnlyError);

        string? path;

        lock (_gate)
            path = _readyPath;

        if (path is null || !File.Exists(path))
            return Ack(context, "update.install.result", false, "There is no downloaded update.");

        try
        {
            // Let this process release its own executable before the MSI replaces it. cmd waits two
            // seconds, by which point the window has closed, and only then starts the installer.
            Process.Start(new ProcessStartInfo("cmd.exe",
                $"/c ping 127.0.0.1 -n 3 >nul & msiexec /i \"{path}\" /passive /norestart")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Could not start the update installer");
            return Ack(context, "update.install.result", false, ex.Message);
        }

        // Let the reply reach the web view before the window goes away.
        _ = Task.Run(async () =>
        {
            await Task.Delay(300);
            _options.Shutdown?.Invoke();
        });

        return Ack(context, "update.install.result", true, null);
    }

    private NativeResponse Open(NativeContext context, string target, string replyType)
    {
        try
        {
            SystemLauncher.Open(target);
            return Ack(context, replyType, true, null);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Could not open {Target}", target);
            return Ack(context, replyType, false, ex.Message);
        }
    }

    private void Notify(string type, object? data) => _options.Notify?.Invoke(type, data);

    private static NativeResponse Ack(NativeContext context, string type, bool ok, string? error) =>
        context.Reply(type, new UpdateAckPayload(ok, error));

    private static bool IsNewer(string latest, string current) =>
        Try.Run(() => Version.Parse(latest) > Version.Parse(current)).GetOrElse(exception =>
        {
            Logger.Debug(exception, "Could not compare versions {Latest} and {Current}", latest, current);
            return false;
        });

    /// <summary>Creates the download folder and drops whatever an earlier run left in it.</summary>
    private void ClearDownloadDirectory()
    {
        var directory = DownloadDirectory;
        Directory.CreateDirectory(directory);

        foreach (var file in Directory.EnumerateFiles(directory))
            Try.Run(() => File.Delete(file));
    }

    /// <summary>Removes a leftover installer from a previous run; it is no longer usable.</summary>
    private void Cleanup()
    {
        if (Directory.Exists(DownloadDirectory))
            ClearDownloadDirectory();
    }
}