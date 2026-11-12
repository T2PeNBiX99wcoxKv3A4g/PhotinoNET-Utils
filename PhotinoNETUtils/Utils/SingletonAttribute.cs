using JetBrains.Annotations;

namespace PhotinoNetUtils.Utils;

/// <summary>
///     Marks a partial class whose singleton members the generator writes: the <c>Instance</c>
///     property, the <see cref="SingletonRegistry" /> entry, the native message subscription and a
///     <c>Log</c> field. Unlike deriving from <see cref="Singleton{T}" />, the class may inherit from
///     anything else.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
[PublicAPI]
public sealed class SingletonAttribute : Attribute;
