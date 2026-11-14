using System.Runtime.Versioning;
using System.Text;
using JetBrains.Annotations;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Utils;
using PropertyStore.Flags;
using PropertyStore.Structures;
using ShellLink;
using ShellLink.Structures;

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
    /// <summary>
    ///     PKEY_AppUserModel_ID: the shell property Windows reads to attribute notifications and to
    ///     group taskbar buttons. A shortcut without it cannot show a toast.
    /// </summary>
    private static readonly Guid AppUserModelFormat = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

    private const uint AppUserModelProperty = 5;

    /// <summary>Creates (or overwrites) a shortcut pointing at <paramref name="targetPath" />.</summary>
    public static void Create(string linkPath, string targetPath, string arguments, string workingDirectory,
        string description, string? appUserModelId = null)
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

        if (appUserModelId is { Length: > 0 })
            shortcut.ExtraData.PropertyStoreDataBlock = AppUserModel(appUserModelId);

        shortcut.WriteToFile(linkPath);
    }

    /// <summary>
    ///     The AppUserModelID in the form Windows stores it in a link: a string property under
    ///     PKEY_AppUserModel_ID in the shell's property store.
    /// </summary>
    private static PropertyStoreDataBlock AppUserModel(string appUserModelId)
    {
        // A VT_LPWSTR value starts with its character count, including the terminator, then UTF-16.
        var text = appUserModelId + "\0";
        var value = new byte[sizeof(uint) + Encoding.Unicode.GetByteCount(text)];
        BitConverter.TryWriteBytes(value.AsSpan(0, sizeof(uint)), (uint)appUserModelId.Length + 1);
        Encoding.Unicode.GetBytes(text, value.AsSpan(sizeof(uint)));

        return new(
        [
            new SerializedPropertyStorage(
            [
                new IntegerName(AppUserModelProperty, new TypedPropertyValue(PropertyType.VT_LPWSTR, value))
            ])
            {
                FormatID = AppUserModelFormat
            }
        ]);
    }

    /// <summary>
    ///     The executable a shortcut starts, or null when the file cannot be read. Most links name it in
    ///     the shell item list; older ones only carry it split across the link info block.
    /// </summary>
    public static string? ReadTarget(string linkPath)
    {
        var shortcut = Try.Run(() => Shortcut.ReadFromFile(linkPath)).GetOrNull();
        if (shortcut is null)
            return null;

        if (shortcut.LinkTargetIDList?.Path is { Length: > 0 } target)
            return target;

        if (shortcut.LinkInfo is not { } info)
            return null;

        var basePath = info.LocalBasePathUnicode is { Length: > 0 } unicode ? unicode : info.LocalBasePath;
        var suffix = info.CommonPathSuffixUnicode is { Length: > 0 } suffixUnicode
            ? suffixUnicode
            : info.CommonPathSuffix;

        return $"{basePath}{suffix}" is { Length: > 0 } path ? path : null;
    }

    /// <summary>The AppUserModelID the shortcut carries, or null when it carries none.</summary>
    public static string? ReadAppUserModelId(string linkPath)
    {
        var shortcut = Try.Run(() => Shortcut.ReadFromFile(linkPath)).GetOrNull();
        if (shortcut?.ExtraData?.PropertyStoreDataBlock is not { } block)
            return null;

        foreach (var storage in block.PropertyStore)
        {
            if (storage.FormatID != AppUserModelFormat)
                continue;

            foreach (var property in storage.PropertyStorage)
                if (property is IntegerName { ID: AppUserModelProperty } named)
                    return named.TypedPropertyValue.Value as string;
        }

        return null;
    }
}