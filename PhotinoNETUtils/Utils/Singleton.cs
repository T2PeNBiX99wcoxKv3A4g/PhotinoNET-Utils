using JetBrains.Annotations;
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
    internal static T? InstanceInternal { get; } = new();
    public static T Instance => InstanceInternal!;
    public static bool IsInitialized => InstanceInternal != null;
}