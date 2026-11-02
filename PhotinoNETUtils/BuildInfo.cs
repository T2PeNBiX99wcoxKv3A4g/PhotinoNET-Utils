using System.Reflection;
using JetBrains.Annotations;

namespace PhotinoNetUtils;

/// <summary>Build metadata shared by Photino desktop applications.</summary>
[PublicAPI]
public static class BuildInfo
{
    /// <summary>The three-part assembly version, or <c>0.0.0</c> when it is unavailable.</summary>
    public static string VersionOf(Assembly assembly) => assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>The lower-case platform identifier used by app interfaces.</summary>
    public static string Platform { get; } = OperatingSystem.IsWindows() ? "windows" :
        OperatingSystem.IsLinux() ? "linux" : "unknown";

    /// <summary>A token that changes when the running executable is replaced.</summary>
    public static string BuildTokenFor(Assembly assembly) => ResolveBuildToken(VersionOf(assembly));

    private static string ResolveBuildToken(string fallback) => Environment.ProcessPath is { Length: > 0 } executable
        ? File.GetLastWriteTimeUtc(executable).Ticks.ToString()
        : fallback;
}
