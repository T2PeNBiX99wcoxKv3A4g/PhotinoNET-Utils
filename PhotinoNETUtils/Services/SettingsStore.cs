using JetBrains.Annotations;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Utils;
using Serilog;

namespace PhotinoNetUtils.Services;

/// <summary>A settings file that can be loaded and saved without knowing its format.</summary>
[PublicAPI]
public interface ISettingsStore<T> where T : class
{
    /// <summary>The file this store reads and writes.</summary>
    string FilePath { get; }

    /// <summary>The last loaded or saved value; the defaults until <see cref="Load" /> runs.</summary>
    T Current { get; }

    /// <summary>Reads the settings, or returns the defaults when the file is missing or unreadable.</summary>
    T Load();

    /// <summary>Reads the settings asynchronously, with the same defaults and fallback as <see cref="Load" />.</summary>
    Task<T> LoadAsync(CancellationToken token = default);

    /// <summary>Writes the settings atomically; returns false when the write failed.</summary>
    Task<bool> SaveAsync(T settings, CancellationToken token = default);
}

/// <summary>
///     Shared file plumbing for a settings document: defaults, an atomic temporary-file write and a
///     one-time migration from an older path. The format is left to <see cref="Deserialize" /> and
///     <see cref="Serialize" />.
/// </summary>
[PublicAPI]
public abstract class SettingsStore<T> : ISettingsStore<T> where T : class
{
    private readonly T _defaults;
    private readonly string? _legacyPath;
    private readonly Func<string, T?>? _legacyReader;

    /// <param name="filePath">The settings file to read and write.</param>
    /// <param name="defaults">Used when the file is missing or cannot be read.</param>
    /// <param name="legacyPath">A file from an older layout, read once when the target does not exist.</param>
    /// <param name="legacyReader">
    ///     Deserializes the legacy file. Defaults to <see cref="Deserialize" />, which is right when
    ///     the old file had the same format; pass a reader when the format changed.
    /// </param>
    protected SettingsStore(string filePath, T defaults, string? legacyPath = null,
        Func<string, T?>? legacyReader = null)
    {
        FilePath = filePath;
        _defaults = defaults;
        _legacyPath = legacyPath;
        _legacyReader = legacyReader;
        Current = defaults;
    }

    public string FilePath { get; }

    public T Current { get; private set; }

    public T Load()
    {
        MigrateLegacy();

        if (!File.Exists(FilePath))
        {
            Current = _defaults;
            return Current;
        }

        return Complete(Try.Run(() => File.ReadAllText(FilePath)));
    }

    public async Task<T> LoadAsync(CancellationToken token = default)
    {
        MigrateLegacy();

        if (!File.Exists(FilePath))
        {
            Current = _defaults;
            return Current;
        }

        return Complete(await Try.Run(() => File.ReadAllTextAsync(FilePath, token)));
    }

    public async Task<bool> SaveAsync(T settings, CancellationToken token = default)
    {
        Current = settings;

        var attempt = await Try.Run(async () =>
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (directory is { Length: > 0 })
                Directory.CreateDirectory(directory);

            var text = Serialize(settings);
            var temporary = FilePath + ".tmp";

            await File.WriteAllTextAsync(temporary, text, token);
            File.Move(temporary, FilePath, true);
        });

        attempt.OnFailure(ex => Log.Warning(ex, "Failed to write {File}", FilePath));
        return attempt.IsSuccess;
    }

    private T Complete(Result<string> text)
    {
        if (!text.IsSuccess)
        {
            text.OnFailure(ex => Log.Warning(ex, "Failed to read {File}", FilePath));
            Current = _defaults;
            return Current;
        }

        var parsed = Try.Run(() => Deserialize(text.Value!));
        if (parsed is { IsSuccess: true, Value: { } loaded })
        {
            Current = loaded;
            return Current;
        }

        parsed.OnFailure(ex => Log.Warning(ex, "Failed to read {File}", FilePath));
        Current = _defaults;
        return Current;
    }

    /// <summary>Reads one document; return null when it cannot be understood.</summary>
    protected abstract T? Deserialize(string text);

    /// <summary>Writes one document, including any header the format wants.</summary>
    protected abstract string Serialize(T settings);

    /// <summary>
    ///     Reads the older file, writes it in the current format and removes it. A legacy file in a
    ///     different format is handled by <c>legacyReader</c>.
    /// </summary>
    private void MigrateLegacy()
    {
        if (_legacyPath is not { Length: > 0 } legacy || !File.Exists(legacy) || File.Exists(FilePath))
            return;

        var text = Try.Run(() => File.ReadAllText(legacy)).GetOrNull();
        if (text is null)
            return;

        var reader = _legacyReader ?? Deserialize;
        var value = Try.Run(() => reader(text)).GetOrNull();
        if (value is null)
            return;

        Try.Run(() =>
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (directory is { Length: > 0 })
                Directory.CreateDirectory(directory);

            File.WriteAllText(FilePath, Serialize(value));
            File.Delete(legacy);
        }).OnFailure(ex => Log.Warning(ex, "Could not move the settings from {Legacy}", legacy));

        Current = value;
    }
}