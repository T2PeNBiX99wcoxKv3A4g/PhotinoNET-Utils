using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using JetBrains.Annotations;

namespace PhotinoNetUtils.Settings;

/// <summary>
///     Marks a settings model for the source generator. The generator registers its
///     <see cref="SettingAttribute" /> comments at startup, without reflection.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
[PublicAPI]
public sealed class SettingsModelAttribute : Attribute;

/// <summary>Describes one settings property for the generated document.</summary>
[AttributeUsage(AttributeTargets.Property)]
[PublicAPI]
public sealed class SettingAttribute : Attribute
{
    /// <summary>The comment written above the property. The writer adds the <c>#</c>.</summary>
    public string? Comment { get; init; }

    /// <summary>Overrides the key; by default the property name is camel-cased.</summary>
    public string? Key { get; init; }

    /// <summary>Sort order for the comments; the serializer decides the document order.</summary>
    public int Order { get; init; }

    /// <summary>
    ///     Smallest value a numeric setting may hold. The generator clamps it on load and before a
    ///     save; <see cref="double.NaN" /> (the default) leaves the value alone.
    /// </summary>
    public double Min { get; init; } = double.NaN;

    /// <summary>
    ///     Largest value a numeric setting may hold. The generator clamps it on load and before a
    ///     save; <see cref="double.NaN" /> (the default) leaves the value alone.
    /// </summary>
    public double Max { get; init; } = double.NaN;
}

/// <summary>One property as the settings document writer sees it.</summary>
[PublicAPI]
public sealed record SettingDefinition(string Key, string Comment);

/// <summary>
///     Registration point for the generated metadata. The source generator fills it from a module
///     initializer; models without the generator fall back to reflection.
/// </summary>
[PublicAPI]
public static class SettingsMetadataRegistry
{
    private static readonly Dictionary<Type, SettingDefinition[]> Entries = new();
    private static readonly Lock Gate = new();

    public static void Register(Type type, SettingDefinition[] definitions)
    {
        lock (Gate)
            Entries[type] = definitions;
    }

    public static SettingDefinition[] Get(Type type)
    {
        lock (Gate)
            return Entries.TryGetValue(type, out var definitions) ? definitions : [];
    }
}

/// <summary>Reads <see cref="SettingAttribute" /> from a settings model that has no generated metadata.</summary>
internal static class SettingMetadata
{
    public static SettingDefinition[] Read(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
        Type type,
        JsonNamingPolicy? namingPolicy)
    {
        var policy = namingPolicy ?? JsonNamingPolicy.CamelCase;

        return
        [
            .. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => new
                {
                    Attribute = property.GetCustomAttribute<SettingAttribute>(),
                    Key = policy.ConvertName(property.Name)
                })
                .Where(item => item.Attribute?.Comment is { Length: > 0 })
                .OrderBy(item => item.Attribute!.Order)
                .Select(item => new SettingDefinition(item.Attribute!.Key ?? item.Key, item.Attribute.Comment!))
        ];
    }
}
