using JetBrains.Annotations;

namespace PhotinoNetUtils.Messaging;

/// <summary>
///     Marks a method that answers one native message type. The source generator adds a
///     <c>__HandleGeneratedMessageAsync</c> dispatcher to the containing partial class.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
[PublicAPI]
public sealed class NativeMessageAttribute(string type) : Attribute
{
    /// <summary>The request type, e.g. <c>settings.get</c>.</summary>
    public string Type { get; } = type;
}