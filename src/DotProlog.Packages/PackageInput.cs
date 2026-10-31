using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DotProlog.Packages;

internal static partial class PackageInput
{
    public static FileStream OpenRead(string path)
    {
        var full = Path.GetFullPath(path);
        return OpenRead(Path.GetDirectoryName(full)!, Path.GetFileName(full));
    }

    public static FileStream OpenRead(string root, string relative)
    {
        var path = PackageFiles.Inside(root, relative);
        var parts = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar);
        List<SafeFileHandle> parents = [];
        SafeFileHandle? file = null;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var current = Path.GetFullPath(root);
                parents.Add(OpenWindows(current, directory: true));
                foreach (var part in parts[..^1])
                {
                    current = Path.Combine(current, part);
                    parents.Add(OpenWindows(current, directory: true));
                }
                file = OpenWindows(path, directory: false);
            }
            else
            {
                if (
                    (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
                    || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64)
                )
                    throw new PlatformNotSupportedException("Package inputs require Windows or Linux/macOS x64/ARM64.");
                var flags = OperatingSystem.IsMacOS() ? 0x1000104 : 0xA0800; // CLOEXEC | NOFOLLOW | NONBLOCK
                var directoryFlag = OperatingSystem.IsMacOS() ? 0x100000 : 0x10000;
                parents.Add(Checked(Open(Path.GetFullPath(root), flags | directoryFlag), path));
                foreach (var part in parts[..^1])
                    parents.Add(Checked(OpenAt(parents[^1], part, flags | directoryFlag), path));
                file = Checked(OpenAt(parents[^1], parts[^1], flags), path);
                int result;
                NativeStat status;
                if (OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.X64)
                    result = FStatDarwin(file, out status);
                else
                    result = FStat(file, out status);
                if (result != 0)
                    throw Error(path);
                var mode =
                    OperatingSystem.IsMacOS() ? status.DarwinMode
                    : RuntimeInformation.ProcessArchitecture == Architecture.X64 ? status.LinuxX64Mode
                    : status.LinuxArm64Mode;
                if ((mode & 0xF000) != 0x8000)
                    throw new InvalidDataException($"Package input is not a regular file: {path}");
            }
            var stream = new FileStream(file, FileAccess.Read);
            file = null;
            return stream;
        }
        finally
        {
            file?.Dispose();
            foreach (var parent in parents)
                parent.Dispose();
        }
    }

    private static SafeFileHandle OpenWindows(string path, bool directory)
    {
        var handle = CreateFile(path, directory ? 0 : 0x80000000, directory ? 3u : 1u, 0, 3, 0x02200000, 0);
        if (handle.IsInvalid)
        {
            var error = Error(path);
            handle.Dispose();
            throw error;
        }
        try
        {
            var attributes = File.GetAttributes(handle);
            if (
                (attributes & FileAttributes.ReparsePoint) != 0
                || ((attributes & FileAttributes.Directory) != 0) != directory
                || GetFileType(handle) != 1
            )
                throw new InvalidDataException($"Package input is not a regular file or directory: {path}");
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static SafeFileHandle Checked(int descriptor, string path)
    {
        if (descriptor < 0)
        {
            var error = Marshal.GetLastPInvokeError();
            // ENOTDIR also covers opening a symlink parent with DIRECTORY | NOFOLLOW.
            if (error == 20 || error == (OperatingSystem.IsMacOS() ? 62 : 40))
                throw new InvalidDataException($"Symbolic links and non-directory parents are not package inputs: {path}");
            throw Error(path);
        }
        return new SafeFileHandle(descriptor, ownsHandle: true);
    }

    private static IOException Error(string path) =>
        new($"Cannot open regular package input: {path}", new Win32Exception(Marshal.GetLastPInvokeError()));

    // The native structs fit in this buffer. Only the ABI-specific mode fields are consumed.
    // Darwin stat64: mode_t at 4; Linux stat: mode_t at 24 (x64), 16 (ARM64).
    // Layouts: apple-oss-distributions/xnu bsd/sys/stat.h and the glibc/musl Linux stat ABIs.
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct NativeStat
    {
        [FieldOffset(4)]
        public ushort DarwinMode;

        [FieldOffset(24)]
        public uint LinuxX64Mode;

        [FieldOffset(16)]
        public uint LinuxArm64Mode;
    }

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int Open(string path, int flags);

    [LibraryImport("libc", EntryPoint = "openat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int OpenAt(SafeFileHandle directory, string path, int flags);

    [LibraryImport("libc", EntryPoint = "fstat", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int FStat(SafeFileHandle file, out NativeStat status);

    [LibraryImport("libc", EntryPoint = "fstat$INODE64", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int FStatDarwin(SafeFileHandle file, out NativeStat status);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial SafeFileHandle CreateFile(
        string path,
        uint access,
        uint share,
        nint security,
        uint creation,
        uint flags,
        nint template
    );

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetFileType(SafeFileHandle file);
}
