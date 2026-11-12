using JetBrains.Annotations;
using Tomlyn;

namespace PhotinoNetUtils.Services;

/// <summary>A settings file stored as TOML, through the caller's source-generated serializer.</summary>
[PublicAPI]
public sealed class TomlSettingsStore<T> : SettingsStore<T> where T : class
{
    private readonly string? _header;
    private readonly TomlTypeInfo<T> _typeInfo;

    /// <summary>Creates a store for <c>settings.toml</c> in the app's settings folder.</summary>
    public static TomlSettingsStore<T> ForApp(string appName, T defaults, TomlTypeInfo<T> typeInfo,
        Func<string, T?>? legacyReader = null, string? legacyPath = null, string? header = null) =>
        new(Path.Combine(AppPaths.SettingsDirectory(appName), "settings.toml"), defaults, typeInfo,
            legacyReader, legacyPath, header);

    /// <param name="filePath">The settings file to read and write.</param>
    /// <param name="defaults">Used when the file is missing or cannot be read.</param>
    /// <param name="typeInfo">The source-generated serializer info for <typeparamref name="T" />.</param>
    /// <param name="legacyReader">Reads an older file in a different format, when there is one.</param>
    /// <param name="legacyPath">The old file, read once when the TOML file does not exist yet.</param>
    /// <param name="header">A comment block written before the document on every save.</param>
    public TomlSettingsStore(string filePath, T defaults, TomlTypeInfo<T> typeInfo,
        Func<string, T?>? legacyReader = null, string? legacyPath = null, string? header = null)
        : base(filePath, defaults, legacyPath, legacyReader)
    {
        _typeInfo = typeInfo;
        _header = header;
    }

    protected override T? Deserialize(string text) => TomlSerializer.Deserialize(text, _typeInfo);

    protected override string Serialize(T settings)
    {
        var document = TomlSerializer.Serialize(settings, _typeInfo);

        return string.IsNullOrWhiteSpace(_header)
            ? document
            : _header.TrimEnd() + Environment.NewLine + Environment.NewLine + document;
    }
}
