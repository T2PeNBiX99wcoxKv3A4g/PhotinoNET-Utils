using JetBrains.Annotations;
using PhotinoNetUtils.Messaging;
using Serilog;

namespace PhotinoNetUtils.Utils;

[PublicAPI]
public interface ISingleton
{
}

/// <summary>
///     Base class for process-wide services. It registers the instance and subscribes native message
///     handlers while it is created.
/// </summary>
/// <remarks>
///     Prefer <see cref="SingletonAttribute" />: the generator writes the same members into the class,
///     so the class keeps its own base class and a second <c>new</c> becomes a compile error.
/// </remarks>
[Obsolete("Use [Singleton]; the generator writes the same members into the class without taking its base class.")]
[PublicAPI]
public abstract class Singleton<T> : ISingleton where T : Singleton<T>, new()
{
    protected readonly ILogger Log = Serilog.Log.ForContext<T>();

    /// <summary>
    ///     A singleton is registered in <see cref="SingletonRegistry" /> while it is created, so the
    ///     app can walk every service without reflection. One that answers native messages also
    ///     starts listening, so its class needs no wiring.
    /// </summary>
    protected Singleton()
    {
        SingletonRegistry.Register(this);

        if (this is INativeMessageHandler handler)
            NativeBridge.Subscribe(handler);
    }

    internal static T? InstanceInternal { get; } = new();
    public static T Instance => InstanceInternal!;
    public static bool IsInitialized => InstanceInternal != null;
}