using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using JetBrains.Annotations;
using PhotinoNetUtils.Utils;

namespace PhotinoNetUtils.Platform;

/// <summary>
///     Creates the link kinds the platform understands: symbolic links for files and folders, hard
///     links for files on the same volume, and (on Windows) directory junctions. Junctions and hard
///     links need no special rights on Windows; a symbolic link needs Developer Mode or an elevated
///     process. On Linux both link kinds map to the standard file system calls.
/// </summary>
[PublicAPI]
public static partial class FileLinkCreation
{
    public const string SymbolicFile = "symbolic-file";
    public const string SymbolicFolder = "symbolic-folder";
    public const string Hard = "hard";
    public const string Junction = "junction";

    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000;
    private const uint OpenReparsePoint = 0x00200000;
    private const uint FsctlSetReparsePoint = 0x000900A4;
    private const uint MountPointTag = 0xA0000003;
    private const uint SymbolicLinkDirectory = 0x1;
    private const uint SymbolicLinkAllowUnprivileged = 0x2;
    public const int ErrorAccessDenied = 5;
    private const int ErrorInvalidParameter = 87;
    public const int ErrorPrivilegeNotHeld = 1314;
    private static readonly IntPtr InvalidHandle = new(-1);

    /// <summary>Creates <paramref name="link" /> pointing at <paramref name="source" />.</summary>
    public static Result Create(string kind, string source, string link, bool elevated = false)
    {
        if (!Directory.Exists(Path.GetDirectoryName(link)))
            return new(false, "The folder for the link does not exist.", 0);

        if (File.Exists(link) || Directory.Exists(link))
            return new(false, "Something with that name already exists there.", 0);

        if (OperatingSystem.IsWindows())
            return CreateWindows(kind, source, link, elevated);

        if (OperatingSystem.IsLinux())
            return CreateLinux(kind, source, link);

        return new(false, "Links are not supported on this platform.", 0);
    }

    [SupportedOSPlatform("windows")]
    private static Result CreateWindows(string kind, string source, string link, bool elevated)
    {
        try
        {
            switch (kind)
            {
                case SymbolicFile:
                    CreateSymbolicLink(link, source, false, elevated);
                    return new(true, "", 0);
                case SymbolicFolder:
                    CreateSymbolicLink(link, source, true, elevated);
                    return new(true, "", 0);
                case Hard:
                    if (!CreateHardLink(link, source, IntPtr.Zero))
                        throw new IOException("The hard link could not be created.",
                            new Win32Exception(Marshal.GetLastWin32Error()));
                    return new(true, "", 0);
                case Junction:
                    CreateJunction(link, source);
                    return new(true, "", 0);
                default:
                    return new(false, "Unknown link type.", 0);
            }
        }
        catch (Exception ex)
        {
            var nativeError = ex;
            while (nativeError.InnerException is not null) nativeError = nativeError.InnerException;
            var errorCode = nativeError is Win32Exception win32 ? win32.NativeErrorCode : 0;
            return new(false, ex.Message, errorCode);
        }
    }

    [SupportedOSPlatform("linux")]
    private static Result CreateLinux(string kind, string source, string link)
    {
        try
        {
            switch (kind)
            {
                case SymbolicFile:
                    File.CreateSymbolicLink(link, source);
                    return new(true, "", 0);
                case SymbolicFolder:
                    Directory.CreateSymbolicLink(link, source);
                    return new(true, "", 0);
                case Hard:
                    if (LinkNative(source, link) != 0)
                        return new(false, "The hard link could not be created.",
                            Marshal.GetLastWin32Error());
                    return new(true, "", 0);
                case Junction:
                    return new(false, "Directory junctions are a Windows feature; use a symbolic link.", 0);
                default:
                    return new(false, "Unknown link type.", 0);
            }
        }
        catch (Exception ex)
        {
            return new(false, ex.Message, 0);
        }
    }

    private static void CreateSymbolicLink(string link, string target, bool directory, bool elevated)
    {
        var flags = directory ? SymbolicLinkDirectory : 0u;
        if (!elevated) flags |= SymbolicLinkAllowUnprivileged;

        if (CreateSymbolicLinkNative(link, target, flags)) return;

        var error = Marshal.GetLastWin32Error();

        // The unprivileged-create flag is not recognized by pre-Creators-Update Windows 10 builds.
        // Retry without it; if privilege is required, the caller can ask the existing UAC helper.
        if (!elevated && error == ErrorInvalidParameter)
        {
            if (CreateSymbolicLinkNative(link, target, directory ? SymbolicLinkDirectory : 0u)) return;
            error = Marshal.GetLastWin32Error();
        }

        throw new IOException("The symbolic link could not be created.", new Win32Exception(error));
    }

    private static void CreateJunction(string link, string target)
    {
        Directory.CreateDirectory(link);

        var handle = CreateFile(link, GenericWrite, 0, IntPtr.Zero, OpenExisting,
            BackupSemantics | OpenReparsePoint, IntPtr.Zero);

        if (handle == InvalidHandle)
        {
            Directory.Delete(link);
            throw new IOException("The junction could not be opened.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }

        var buffer = IntPtr.Zero;

        try
        {
            // The substitute name is the NT path the file system follows; the print name is what
            // Explorer shows.
            var substitute = Encoding.Unicode.GetBytes(@"\??\" + target);
            var print = Encoding.Unicode.GetBytes(target);
            var pathLength = substitute.Length + 2 + print.Length + 2;
            var size = 8 + 8 + pathLength;

            buffer = Marshal.AllocHGlobal(size);

            using (var stream = new MemoryStream(size))
            using (var writer = new BinaryWriter(stream, Encoding.Unicode))
            {
                writer.Write(MountPointTag);
                writer.Write((ushort)(8 + pathLength));
                writer.Write((ushort)0);
                writer.Write((ushort)0);
                writer.Write((ushort)substitute.Length);
                writer.Write((ushort)(substitute.Length + 2));
                writer.Write((ushort)print.Length);
                writer.Write(substitute);
                writer.Write((ushort)0);
                writer.Write(print);
                writer.Write((ushort)0);

                Marshal.Copy(stream.ToArray(), 0, buffer, size);
            }

            if (!DeviceIoControl(handle, FsctlSetReparsePoint, buffer, (uint)size, IntPtr.Zero, 0,
                    out _, IntPtr.Zero))
                throw new IOException("The junction could not be written.",
                    new Win32Exception(Marshal.GetLastWin32Error()));
        }
        catch
        {
            Try.Run(() => Directory.Delete(link));
            throw;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
                Marshal.FreeHGlobal(buffer);

            CloseHandle(handle);
        }
    }

    [LibraryImport("libc", EntryPoint = "link", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int LinkNative(string existing, string link);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16,
        SetLastError = true)]
    private static partial IntPtr CreateFile(string name, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string link, string existing, IntPtr security);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateSymbolicLinkW", StringMarshalling = StringMarshalling.Utf16,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateSymbolicLinkNative(string link, string target, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(IntPtr device, uint control, IntPtr input, uint inputSize,
        IntPtr output, uint outputSize, out uint returned, IntPtr overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    public readonly record struct Result(bool Ok, string Error, int ErrorCode);
}