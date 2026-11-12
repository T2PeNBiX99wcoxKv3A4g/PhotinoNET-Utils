using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using JetBrains.Annotations;

namespace PhotinoNetUtils.Settings;

/// <summary>Describes one settings property for the generated document.</summary>
[AttributeUsage(AttributeTargets.Property)]
[PublicAPI]
public sealed class SettingAttribute : Attribute
{
    /// <summary>The comment written above the property. The writer adds the <c>#</c>.</summary>
    public string? Comment { get; init; }

    /// <summary>Sort order for the comments; the serializer decides the document order.</summary>
    public int Order { get; init; }
}

/// <summary>One property as the settings document writer sees it.</summary>
internal sealed record SettingDefinition(string Key, string Comment);

/// <summary>Reads <see cref="SettingAttribute" /> from a settings model.</summary>
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
                .Select(item => new SettingDefinition(item.Key, item.Attribute!.Comment!))
        ];
    }
}
