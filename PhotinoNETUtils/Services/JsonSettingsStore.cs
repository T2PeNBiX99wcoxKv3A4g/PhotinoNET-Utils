using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using JetBrains.Annotations;

namespace PhotinoNetUtils.Services;

/// <summary>A settings file stored as JSON, through the caller's source-generated serializer.</summary>
[PublicAPI]
public sealed class JsonSettingsStore<T> : SettingsStore<T> where T : class
{
    private readonly JsonTypeInfo<T> _typeInfo;

    /// <summary>Creates a store for <c>settings.json</c> in the app's settings folder.</summary>
    public static JsonSettingsStore<T> ForApp(string appName, T defaults, JsonTypeInfo<T> typeInfo,
        string? legacyPath = null) =>
        new(Path.Combine(AppPaths.SettingsDirectory(appName), "settings.json"), defaults, typeInfo, legacyPath);

    public JsonSettingsStore(string filePath, T defaults, JsonTypeInfo<T> typeInfo, string? legacyPath = null)
        : base(filePath, defaults, legacyPath)
    {
        _typeInfo = typeInfo;
    }

    protected override T? Deserialize(string text) => JsonSerializer.Deserialize(text, _typeInfo);

    protected override string Serialize(T settings) => JsonSerializer.Serialize(settings, _typeInfo);
}
