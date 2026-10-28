using System.Runtime.Versioning;
using JetBrains.Annotations;
using ShellLink;

namespace PhotinoNetUtils.Platform;

/// <summary>
///     Creates a Windows shell link (<c>.lnk</c>) pointing at an executable, for the per-user Startup
///     folder or the Start Menu.
/// </summary>
/// <remarks>
///     The format is not assembled here: a link only resolves when its <c>LinkTargetIDList</c> holds
///     shell-namespace items with their metadata, which is far more than a path string, so the byte
///     layout is left to the <c>securifybv.ShellLink</c> library.
/// </remarks>
[PublicAPI]
[SupportedOSPlatform("windows")]
public static class WindowsShortcut
{
    /// <summary>Creates (or overwrites) a shortcut pointing at <paramref name="targetPath" />.</summary>
    public static void Create(string linkPath, string targetPath, string arguments, string workingDirectory,
        string description)
    {
        if (Path.GetDirectoryName(linkPath) is { Length: > 0 } directory)
            Directory.CreateDirectory(directory);

        var shortcut = new Shortcut
        {
            LinkTargetIDList = new()
            {
                Path = targetPath
            },
            // Without an explicit index the link stores -1, which points at no icon at all, so the
            // shortcut turns up blank.
            IconIndex = 0,
            StringData = new()
            {
                NameString = description,
                WorkingDir = workingDirectory,
                CommandLineArguments = arguments,
                // The target's own icon, which is the one the executable was built with.
                IconLocation = targetPath
            }
        };

        shortcut.WriteToFile(linkPath);
    }
}
