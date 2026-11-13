using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JetBrains.Annotations;

namespace PhotinoNetUtils.Platform;

/// <summary>
///     The per-directory case-sensitivity flag NTFS keeps on a folder. Windows only lets it be
///     turned on for an empty folder, so callers create the folder first and set the flag directly
///     afterwards instead of shelling out to <c>fsutil</c>.
/// </summary>
[SupportedOSPlatform("windows")]
[PublicAPI]
public static partial class CaseSensitivity
{

    private const uint FileReadAttributes = 0x00000080;
    private const uint FileWriteAttributes = 0x00000100;
    private const uint ShareReadWriteDelete = 0x00000007;
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000;
    private const int FileCaseSensitiveInfo = 23;
    private const uint CaseSensitiveFlag = 1;
    private static readonly IntPtr InvalidHandle = new(-1);

    /// <summary>Whether names in this folder are compared with case.</summary>
    public static bool Get(string path)
    {
        var handle = Open(path, FileReadAttributes);
        if (handle == InvalidHandle)
            return false;

        try
        {
            return GetFileInformationByHandleEx(handle, FileCaseSensitiveInfo, out var flags, sizeof(uint))
                   && (flags & CaseSensitiveFlag) != 0;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>Turns the flag on or off. Windows refuses to turn it on for a non-empty folder.</summary>
    public static void Set(string path, bool enabled)
    {
        var handle = Open(path, FileWriteAttributes);
        if (handle == InvalidHandle)
            throw new IOException($"Could not open '{path}'.", new Win32Exception(Marshal.GetLastWin32Error()));

        try
        {
            var flags = enabled ? CaseSensitiveFlag : 0u;

            if (!SetFileInformationByHandle(handle, FileCaseSensitiveInfo, ref flags, sizeof(uint)))
                throw new IOException($"Could not change the case sensitivity of '{path}'.",
                    new Win32Exception(Marshal.GetLastWin32Error()));
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>True when the folder has no entries at all.</summary>
    public static bool IsEmpty(string path) => !Directory.EnumerateFileSystemEntries(path).Any();

    private static IntPtr Open(string path, uint access) =>
        CreateFile(path, access, ShareReadWriteDelete, IntPtr.Zero, OpenExisting, BackupSemantics, IntPtr.Zero);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16,
        SetLastError = true)]
    private static partial IntPtr CreateFile(string name, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(IntPtr handle, int infoClass, out uint info, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFileInformationByHandle(IntPtr handle, int infoClass, ref uint info, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);
}