using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using JetBrains.Annotations;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Utils;
using Serilog;

namespace PhotinoNetUtils.Services;

/// <summary>
///     Loads and stores one JSON settings object, leaving validation and the bridge messages to the
///     app.
/// </summary>
/// <remarks>
///     <para>
///         A missing or unreadable file falls back to the defaults instead of failing the launch. The
///         write goes through a temporary file and a move, so a crash midway never leaves a truncated
///         settings file.
///     </para>
///     <para>
///         <typeparamref name="T" /> is deserialized through the caller's source-generated
///         <see cref="JsonTypeInfo{T}" />, so trimming never drops a property.
///     </para>
/// </remarks>
[PublicAPI]
public sealed class JsonSettingsStore<T> where T : class
{
    private readonly T _defaults;
    private readonly string? _legacyPath;
    private readonly JsonTypeInfo<T> _typeInfo;

    /// <summary>Creates a store for <c>settings.json</c> in the app's settings folder.</summary>
    public static JsonSettingsStore<T> ForApp(string appName, T defaults, JsonTypeInfo<T> typeInfo,
        string? legacyPath = null) =>
        new(Path.Combine(AppPaths.SettingsDirectory(appName), "settings.json"), defaults, typeInfo, legacyPath);

    /// <param name="filePath">The settings file to read and write.</param>
    /// <param name="defaults">Used when the file is missing or cannot be read.</param>
    /// <param name="typeInfo">The source-generated serializer info for <typeparamref name="T" />.</param>
    /// <param name="legacyPath">A file from an older layout, moved into place once when it exists.</param>
    public JsonSettingsStore(string filePath, T defaults, JsonTypeInfo<T> typeInfo, string? legacyPath = null)
    {
        FilePath = filePath;
        _defaults = defaults;
        _typeInfo = typeInfo;
        _legacyPath = legacyPath;
        Current = defaults;
    }

    /// <summary>The file this store reads and writes.</summary>
    public string FilePath { get; }

    /// <summary>The last loaded or saved value; the defaults until <see cref="Load" /> runs.</summary>
    public T Current { get; private set; }

    /// <summary>Moves a legacy file into place once, then reads the settings, or returns the defaults.</summary>
    public T Load()
    {
        MoveLegacy();

        if (!File.Exists(FilePath))
        {
            Current = _defaults;
            return Current;
        }

        var attempt = Try.Run(() =>
            JsonSerializer.Deserialize(File.ReadAllBytes(FilePath), _typeInfo));

        if (attempt is { IsSuccess: true, Value: { } stored })
        {
            Current = stored;
            return Current;
        }

        attempt.OnFailure(ex => Log.Warning(ex, "Failed to read {File}", FilePath));
        Current = _defaults;
        return Current;
    }

    /// <summary>Writes the settings through a temporary file; returns false when the write failed.</summary>
    public async Task<bool> SaveAsync(T settings, CancellationToken token = default)
    {
        Current = settings;

        var attempt = await Try.Run(async () =>
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (directory is { Length: > 0 })
                Directory.CreateDirectory(directory);

            var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, _typeInfo);
            var temporary = FilePath + ".tmp";

            await File.WriteAllBytesAsync(temporary, bytes, token);
            File.Move(temporary, FilePath, true);
        });

        attempt.OnFailure(ex => Log.Warning(ex, "Failed to write {File}", FilePath));
        return attempt.IsSuccess;
    }

    private void MoveLegacy()
    {
        if (_legacyPath is not { Length: > 0 } legacy || !File.Exists(legacy) || File.Exists(FilePath))
            return;

        Try.Run(() =>
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (directory is { Length: > 0 })
                Directory.CreateDirectory(directory);

            File.Move(legacy, FilePath, true);
        }).OnFailure(ex => Log.Warning(ex, "Could not move the settings from {Legacy}", legacy));
    }
}
