using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace KeyLoad.Storage.IO;

internal static partial class OfflineMacFileSystem
{
    private const uint TypeMask = 0xF000;

    internal static OfflineFileMetadata InspectPath(string path) => ReadPath(path);

    internal static OfflineFileMetadata InspectHandle(SafeFileHandle handle) => ReadHandle(
        handle.DangerousGetHandle().ToInt32());

    internal static int Open(string path, int flags) => OpenPath(path, flags, 0);

    internal static int Lock(int descriptor, int operation) => Flock(descriptor, operation);

    private static OfflineFileMetadata ReadPath(string path)
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            if (LStatX64(path, out var value) != 0)
            { throw OfflineNativeErrors.FromErrno(Marshal.GetLastPInvokeError(), isMac: true); }
            return Convert(value);
        }
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            if (LStatArm64(path, out var value) != 0)
            { throw OfflineNativeErrors.FromErrno(Marshal.GetLastPInvokeError(), isMac: true); }
            return Convert(value);
        }
        throw OfflineNativeErrors.Unsupported();
    }

    private static OfflineFileMetadata ReadHandle(int descriptor)
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            if (FStatX64(descriptor, out var value) != 0)
            { throw OfflineNativeErrors.FromErrno(Marshal.GetLastPInvokeError(), isMac: true); }
            return Convert(value);
        }
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            if (FStatArm64(descriptor, out var value) != 0)
            { throw OfflineNativeErrors.FromErrno(Marshal.GetLastPInvokeError(), isMac: true); }
            return Convert(value);
        }
        throw OfflineNativeErrors.Unsupported();
    }

    private static OfflineFileMetadata Convert(MacStat value)
    {
        if (value.Size < 0)
        { throw OfflineNativeErrors.Unsupported(); }
        return new(new(unchecked((uint)value.Device), value.Inode, value.Size), (uint)(value.Mode & TypeMask));
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libc", EntryPoint = "lstat$INODE64", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int LStatX64(string path, out MacStat value);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libc", EntryPoint = "fstat$INODE64", SetLastError = true)]
    private static partial int FStatX64(int descriptor, out MacStat value);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libc", EntryPoint = "lstat", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int LStatArm64(string path, out MacStat value);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libc", EntryPoint = "fstat", SetLastError = true)]
    private static partial int FStatArm64(int descriptor, out MacStat value);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int OpenPath(string path, int flags, uint mode);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static partial int Flock(int descriptor, int operation);

    [StructLayout(LayoutKind.Explicit, Size = 144)]
    private struct MacStat
    {
        [FieldOffset(0)] internal int Device;
        [FieldOffset(4)] internal ushort Mode;
        [FieldOffset(8)] internal ulong Inode;
        [FieldOffset(96)] internal long Size;
    }
}
