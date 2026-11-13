using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JetBrains.Annotations;

namespace PhotinoNetUtils.Platform;

/// <summary>
///     Explorer's "hidden items" and "protected operating system files" options, so a listing shows
///     what the user's file manager shows. The hidden flag can be overridden for this session without
///     touching the system setting; system files always follow Explorer.
/// </summary>
[PublicAPI]
public static partial class FileVisibility
{

    private const uint RrfRtRegDword = 0x00000010;
    private const string AdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    /// <summary>How long a registry answer is reused; Explorer can change it while the app runs.</summary>
    private const long CacheMilliseconds = 2000;

    private static readonly IntPtr HkeyCurrentUser = new(0x80000001);

    private static bool? _hiddenOverride;

    private static long _readAt = long.MinValue / 2;
    private static bool _hidden;
    private static bool _system;

    /// <summary>"Hidden" is 1 when hidden items are shown and 2 (the default) when they are not.</summary>
    public static bool ShowHidden => _hiddenOverride ?? FollowsSystemHidden;

    /// <summary>What Explorer itself says, regardless of this session's override.</summary>
    public static bool FollowsSystemHidden =>
        OperatingSystem.IsWindows() && ReadCached().Hidden;

    /// <summary>"ShowSuperHidden" is 1 only when protected operating system files are shown.</summary>
    public static bool ShowSystem =>
        OperatingSystem.IsWindows() && ReadCached().System;

    /// <summary>Flip ordinary hidden files for this session; system files are left alone.</summary>
    public static bool ToggleHidden()
    {
        _hiddenOverride = !ShowHidden;
        return ShowHidden;
    }

    /// <summary>Applies the user's saved choice when a process starts; null follows the system.</summary>
    public static void SetHidden(bool? show) => _hiddenOverride = show;

    /// <summary>
    ///     Two registry reads are enough for every listing that arrives in the same couple of
    ///     seconds, instead of reading them again for each refresh.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static (bool Hidden, bool System) ReadCached()
    {
        var now = Environment.TickCount64;
        if (now - _readAt < CacheMilliseconds)
            return (_hidden, _system);

        _hidden = ReadDword("Hidden") == 1;
        _system = ReadDword("ShowSuperHidden") == 1;
        _readAt = now;

        return (_hidden, _system);
    }

    [SupportedOSPlatform("windows")]
    private static uint ReadDword(string name)
    {
        uint value = 0;
        uint size = sizeof(uint);

        // A missing value or an unexpected type is not an error: the default is "off".
        return RegGetValue(HkeyCurrentUser, AdvancedKey, name, RrfRtRegDword, IntPtr.Zero, ref value, ref size) == 0
            ? value
            : 0;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "RegGetValueW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegGetValue(IntPtr key, string subKey, string value, uint flags, IntPtr type,
        ref uint data, ref uint size);
}