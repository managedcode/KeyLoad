using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace KeyLoad.Storage.IO;

internal static partial class OfflineMacFileSystem
{
    private const string NativeLibrary = "libc";
    private const string OpenEntryPoint = "open";
    private const string FileLockEntryPoint = "flock";
    private const uint NoCreationPermissions = 0;
    private const int NativeCallSuccess = 0;
    private const string IntelPathStatEntryPoint = "lstat$INODE64";
    private const string IntelHandleStatEntryPoint = "fstat$INODE64";
    private const string ArmPathStatEntryPoint = "lstat";
    private const string ArmHandleStatEntryPoint = "fstat";
    private const long MinimumFileSize = 0;

    private const uint TypeMask = 0xF000;

    internal static OfflineFileMetadata InspectPath(string path) => ReadPath(path);

    internal static OfflineFileMetadata InspectHandle(SafeFileHandle handle) => ReadHandle(
        handle.DangerousGetHandle().ToInt32());

    internal static int Open(string path, int flags) => OpenPath(path, flags, NoCreationPermissions);

    internal static int Lock(int descriptor, int operation) => Flock(descriptor, operation);

    private static OfflineFileMetadata ReadPath(string path)
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            if (LStatX64(path, out var value) != NativeCallSuccess)
            { throw OfflineNativeErrors.FromErrno(Marshal.GetLastPInvokeError(), isMac: true); }
            return Convert(value);
        }
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            if (LStatArm64(path, out var value) != NativeCallSuccess)
            { throw OfflineNativeErrors.FromErrno(Marshal.GetLastPInvokeError(), isMac: true); }
            return Convert(value);
        }
        throw OfflineNativeErrors.Unsupported();
    }

    private static OfflineFileMetadata ReadHandle(int descriptor)
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            if (FStatX64(descriptor, out var value) != NativeCallSuccess)
            { throw OfflineNativeErrors.FromErrno(Marshal.GetLastPInvokeError(), isMac: true); }
            return Convert(value);
        }
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            if (FStatArm64(descriptor, out var value) != NativeCallSuccess)
            { throw OfflineNativeErrors.FromErrno(Marshal.GetLastPInvokeError(), isMac: true); }
            return Convert(value);
        }
        throw OfflineNativeErrors.Unsupported();
    }

    private static OfflineFileMetadata Convert(MacStat value)
    {
        if (value.Size < MinimumFileSize)
        { throw OfflineNativeErrors.Unsupported(); }
        return new(new(unchecked((uint)value.Device), value.Inode, value.Size), (uint)(value.Mode & TypeMask));
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(NativeLibrary, EntryPoint = IntelPathStatEntryPoint, SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int LStatX64(string path, out MacStat value);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(NativeLibrary, EntryPoint = IntelHandleStatEntryPoint, SetLastError = true)]
    private static partial int FStatX64(int descriptor, out MacStat value);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(NativeLibrary, EntryPoint = ArmPathStatEntryPoint, SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int LStatArm64(string path, out MacStat value);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(NativeLibrary, EntryPoint = ArmHandleStatEntryPoint, SetLastError = true)]
    private static partial int FStatArm64(int descriptor, out MacStat value);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(NativeLibrary, EntryPoint = OpenEntryPoint, SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int OpenPath(string path, int flags, uint mode);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(NativeLibrary, EntryPoint = FileLockEntryPoint, SetLastError = true)]
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
