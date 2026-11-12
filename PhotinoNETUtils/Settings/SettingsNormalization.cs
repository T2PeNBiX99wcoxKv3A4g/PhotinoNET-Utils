using JetBrains.Annotations;

namespace PhotinoNetUtils.Settings;

/// <summary>Turns one settings model into its normalized copy.</summary>
[PublicAPI]
public delegate object SettingsNormalizer(object settings);

/// <summary>
///     Registration point for the generated range checks. The source generator fills it from a
///     module initializer, so <c>SettingsServiceBase</c> clamps <c>[Setting(Min = ..., Max = ...)]</c>
///     values without reflection and without each app writing the same code.
/// </summary>
[PublicAPI]
public static class SettingsNormalizationRegistry
{
    private static readonly Dictionary<Type, SettingsNormalizer> Entries = new();
    private static readonly Lock Gate = new();

    public static void Register(Type type, SettingsNormalizer normalizer)
    {
        lock (Gate)
            Entries[type] = normalizer;
    }

    /// <summary>Applies the registered normalizer, or returns the model unchanged when there is none.</summary>
    public static object Normalize(object settings)
    {
        SettingsNormalizer? normalizer;

        lock (Gate)
            Entries.TryGetValue(settings.GetType(), out normalizer);

        return normalizer is null ? settings : normalizer(settings);
    }
}

/// <summary>
///     Marks the record the interface reads for a settings model. An analyzer then reports a
///     <c>[Setting]</c> the payload forgot, which is otherwise only noticed in the UI.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
[PublicAPI]
public sealed class SettingsPayloadAttribute(Type model) : Attribute
{
    /// <summary>The settings model this payload mirrors.</summary>
    public Type Model { get; } = model;
}