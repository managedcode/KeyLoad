using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace KeyLoad.Storage.IO;

internal static partial class OfflineLinuxFileSystem
{
    private const string NativeLibrary = "libc";
    private const string OpenEntryPoint = "open";
    private const string FileLockEntryPoint = "flock";
    private const uint NoCreationPermissions = 0;
    private const int NativeCallSuccess = 0;
    private const string StatxEntryPoint = "statx";
    private const int DeviceComponentBitWidth = 32;

    private const int AtCurrentDirectory = -100;
    private const int AtSymlinkNoFollow = 0x100;
    private const int AtEmptyPath = 0x1000;
    private const uint RequiredMask = 0x301;
    private const uint TypeMask = 0xF000;

    internal static OfflineFileMetadata InspectPath(string path) => Read(path, AtSymlinkNoFollow);

    internal static OfflineFileMetadata InspectHandle(SafeFileHandle handle) => Read(string.Empty, AtEmptyPath,
        handle.DangerousGetHandle().ToInt32());

    internal static int Open(string path, int flags) => OpenPath(path, flags, NoCreationPermissions);

    internal static int Lock(int descriptor, int operation) => Flock(descriptor, operation);

    private static OfflineFileMetadata Read(string path, int flags, int directory = AtCurrentDirectory)
    {
        if (Statx(directory, path, flags, RequiredMask, out var value) != NativeCallSuccess)
        { throw OfflineNativeErrors.FromErrno(Marshal.GetLastPInvokeError(), isMac: false); }
        if ((value.Mask & RequiredMask) != RequiredMask || value.Size > long.MaxValue)
        { throw OfflineNativeErrors.Unsupported(); }

        var device = ((ulong)value.DeviceMajor << DeviceComponentBitWidth) | value.DeviceMinor;
        return new(new(device, value.Inode, (long)value.Size), (uint)(value.Mode & TypeMask));
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(NativeLibrary, EntryPoint = StatxEntryPoint, SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Statx(int directory, string path, int flags, uint mask, out LinuxStatx value);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(NativeLibrary, EntryPoint = OpenEntryPoint, SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int OpenPath(string path, int flags, uint mode);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(NativeLibrary, EntryPoint = FileLockEntryPoint, SetLastError = true)]
    private static partial int Flock(int descriptor, int operation);

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxStatx
    {
        [FieldOffset(0)] internal uint Mask;
        [FieldOffset(28)] internal ushort Mode;
        [FieldOffset(32)] internal ulong Inode;
        [FieldOffset(40)] internal ulong Size;
        [FieldOffset(136)] internal uint DeviceMajor;
        [FieldOffset(140)] internal uint DeviceMinor;
    }
}
