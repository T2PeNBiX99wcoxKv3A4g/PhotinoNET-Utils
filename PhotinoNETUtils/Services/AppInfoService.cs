using System.Reflection;
using JetBrains.Annotations;
using PhotinoNetUtils.Messaging;

namespace PhotinoNetUtils.Services;

/// <summary>Body for <c>app.info</c>: the build, the platform, and where the app writes its logs.</summary>
[PublicAPI]
public sealed record AppInfoPayload(string Version, string Platform, string LogDirectory, bool Rounded, bool Debug);

/// <summary>What the shared <c>app.info</c> handler reports.</summary>
[PublicAPI]
public sealed record AppInfoOptions(
    Assembly Assembly,
    string LogDirectory,
    bool Debug,
    Func<bool>? Rounded = null);

/// <summary>Answers <c>app.info</c> with the running build's version, platform and log folder.</summary>
/// <remarks>
///     <c>Debug</c> comes from the app, because the shared library is built once and released; its
///     own compile symbols say nothing about the app that is running. <c>Rounded</c> is evaluated per
///     request, so it can follow a window that draws its own frame on Linux.
/// </remarks>
[PublicAPI]
public sealed partial class AppInfoService : NativeMessageService
{
    private readonly AppInfoOptions _options;

    public AppInfoService(AppInfoOptions options) => _options = options;

    [NativeMessage("app.info")]
    private NativeResponse Info(NativeContext context)
    {
        var rounded = _options.Rounded?.Invoke() ?? false;

        return context.Reply("app.info.result",
            new AppInfoPayload(
                BuildInfo.VersionOf(_options.Assembly),
                BuildInfo.Platform,
                _options.LogDirectory,
                rounded,
                _options.Debug));
    }
}
