using JetBrains.Annotations;
using PhotinoNetUtils.Messaging;
using Serilog;

namespace PhotinoNetUtils.Utils;

[PublicAPI]
public interface ISingleton
{
}

[PublicAPI]
public abstract class Singleton<T> : ISingleton where T : Singleton<T>, new()
{
    protected readonly ILogger Log = Serilog.Log.ForContext<T>();

    /// <summary>
    ///     A singleton that answers native messages starts listening as soon as it is created, so a
    ///     service never has to subscribe by hand. Types without <c>[NativeMessage]</c> do not
    ///     implement the interface and are left alone.
    /// </summary>
    protected Singleton()
    {
        if (this is INativeMessageHandler handler)
            NativeBridge.Subscribe(handler);
    }

    internal static T? InstanceInternal { get; } = new();
    public static T Instance => InstanceInternal!;
    public static bool IsInitialized => InstanceInternal != null;
}