using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JetBrains.Annotations;

namespace PhotinoNetUtils.Platform;

/// <summary>
///     Hard-link counts from Windows. A file with more than one name pointing at the same data is
///     what <c>mklink /H</c> makes; the shell has no cheap managed API for the number.
/// </summary>
[SupportedOSPlatform("windows")]
[PublicAPI]
public static partial class FileLinks
{

    private const uint GenericRead = 0x80000000;
    private const uint ShareReadWriteDelete = 0x00000007;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;
    private static readonly IntPtr InvalidHandle = new(-1);

    /// <summary>How many names point at this file. A file that cannot be opened counts as one.</summary>
    public static int Count(string path)
    {
        // CreateFileW directly is noticeably faster than File.OpenHandle for the one handle per file
        // that the Links column needs.
        var handle = CreateFile(path, GenericRead, ShareReadWriteDelete, IntPtr.Zero, OpenExisting,
            FileAttributeNormal, IntPtr.Zero);

        if (handle == InvalidHandle)
            return 1;

        try
        {
            return GetFileInformationByHandle(handle, out var information)
                ? (int)information.NumberOfLinks
                : 1;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16,
        SetLastError = true)]
    private static partial IntPtr CreateFile(string name, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(IntPtr handle,
        out ByHandleFileInformation information);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    /// <summary><c>FILETIME</c>: two 32-bit halves, so the 4-byte alignment of the native struct holds.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint Low;
        public uint High;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public NativeFileTime CreationTime;
        public NativeFileTime LastAccessTime;
        public NativeFileTime LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}