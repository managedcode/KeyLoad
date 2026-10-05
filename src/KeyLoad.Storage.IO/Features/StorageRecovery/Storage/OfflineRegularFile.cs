using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace KeyLoad.Storage.IO;

internal static class OfflineRegularFile
{
    private const int FirstValidDescriptor = 0;
    private const int NativeCallSuccess = 0;
    private const int NoBufferBytes = 0;
    private const char NullPathCharacter = '\0';
    private const int LinuxReadOnly = 0;
    private const int LinuxReadWrite = 0x2;
    private const int LinuxNoFollow = 0x20000;
    private const int LinuxCloseOnExec = 0x80000;
    private const int MacReadOnly = 0;
    private const int MacReadWrite = 0x2;
    private const int MacNoFollow = 0x100;
    private const int MacCloseOnExec = 0x1000000;
    private const int LockShared = 1;
    private const int LockExclusive = 2;
    private const int LockNonBlocking = 4;
    private const uint RegularType = 0x8000;
    private const int MaximumBufferBytes = 65536;

    internal static void RequireRegular(string path) => _ = Inspect(path);

    internal static OfflineFileIdentity Inspect(string path)
    {
        ValidatePath(path);
        var metadata = InspectNativePath(path);
        if (metadata.Type != RegularType)
        { throw OfflineNativeErrors.InvalidEntry(); }
        return metadata.Identity;
    }

    internal static FileStream Open(string path, FileAccess access, FileShare share, int bufferSize)
    {
        var expected = Inspect(path);
        return OpenWithIdentity(path, expected, access, share, bufferSize);
    }

    internal static FileStream OpenWithIdentity(string path, OfflineFileIdentity expected,
        FileAccess access, FileShare share, int bufferSize)
    {
        ValidateArguments(path, access, share, bufferSize);

        using var lease = OpenNative(path, access);
        try
        {
            VerifyDescriptor(lease.Handle, expected);
            AcquireLock(lease.Handle, share);
            VerifyPath(path, expected);
            return lease.TransferToStream(access, bufferSize);
        }
        catch (Exception error)
        {
            lease.RecordPrimary(error);
            throw;
        }
    }

    private static void VerifyDescriptor(SafeFileHandle handle, OfflineFileIdentity expected)
    {
        var metadata = InspectNativeHandle(handle);
        if (metadata.Type != RegularType)
        { throw OfflineNativeErrors.InvalidEntry(); }
        if (metadata.Identity != expected)
        { throw OfflineNativeErrors.ChangedEntry(); }
    }

    private static void VerifyPath(string path, OfflineFileIdentity expected)
    {
        var metadata = InspectNativePath(path);
        if (metadata.Type != RegularType)
        { throw OfflineNativeErrors.InvalidEntry(); }
        if (metadata.Identity != expected)
        { throw OfflineNativeErrors.ChangedEntry(); }
    }

    private static OfflineNativeHandleLease OpenNative(string path, FileAccess access)
    {
        var (flags, isMac) = OpenFlags(access);
        int descriptor;
        try
        { descriptor = isMac ? OfflineMacFileSystem.Open(path, flags) : OfflineLinuxFileSystem.Open(path, flags); }
        catch (Exception error) when (OfflineNativeErrors.IsUnavailable(error))
        { throw OfflineNativeErrors.Unsupported(); }
        if (descriptor < FirstValidDescriptor)
        { throw OfflineNativeErrors.FromErrno(Marshal.GetLastPInvokeError(), isMac); }
        SafeFileHandle? handle = new(new IntPtr(descriptor), ownsHandle: true);
        Exception? primaryFailure = null;
        try
        {
            var lease = new OfflineNativeHandleLease(handle);
            handle = null;
            return lease;
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally
        { OfflineNativeHandleLease.DisposeUntransferred(ref handle, primaryFailure); }
    }

    private static void AcquireLock(SafeFileHandle handle, FileShare share)
    {
        var isMac = OperatingSystem.IsMacOS();
        var descriptor = handle.DangerousGetHandle().ToInt32();
        var operation = (share == FileShare.Read ? LockShared : LockExclusive) | LockNonBlocking;
        int result;
        try
        { result = isMac ? OfflineMacFileSystem.Lock(descriptor, operation) : OfflineLinuxFileSystem.Lock(descriptor, operation); }
        catch (Exception error) when (OfflineNativeErrors.IsUnavailable(error))
        { throw OfflineNativeErrors.Unsupported(); }
        if (result != NativeCallSuccess)
        { throw OfflineNativeErrors.FromErrno(Marshal.GetLastPInvokeError(), isMac); }
    }

    private static OfflineFileMetadata InspectNativePath(string path)
    {
        try
        { return OperatingSystem.IsMacOS() ? OfflineMacFileSystem.InspectPath(path) : OfflineLinuxFileSystem.InspectPath(path); }
        catch (Exception error) when (OfflineNativeErrors.IsUnavailable(error))
        { throw OfflineNativeErrors.Unsupported(); }
    }

    private static OfflineFileMetadata InspectNativeHandle(SafeFileHandle handle)
    {
        try
        { return OperatingSystem.IsMacOS() ? OfflineMacFileSystem.InspectHandle(handle) : OfflineLinuxFileSystem.InspectHandle(handle); }
        catch (Exception error) when (OfflineNativeErrors.IsUnavailable(error))
        { throw OfflineNativeErrors.Unsupported(); }
    }

    private static (int Flags, bool IsMac) OpenFlags(FileAccess access)
    {
        var isMac = OperatingSystem.IsMacOS();
        var flags = isMac ? MacNoFollow | MacCloseOnExec : LinuxNoFollow | LinuxCloseOnExec;
        if (isMac)
        {
            flags |= access == FileAccess.Read ? MacReadOnly : MacReadWrite;
            flags |= MacNonBlocking;
        }
        else
        {
            flags |= access == FileAccess.Read ? LinuxReadOnly : LinuxReadWrite;
            flags |= LinuxNonBlocking;
        }
        return (flags, isMac);
    }

    private static void ValidateArguments(string path, FileAccess access, FileShare share, int bufferSize)
    {
        ValidatePath(path);
        if (access is not (FileAccess.Read or FileAccess.ReadWrite)
            || share is not (FileShare.Read or FileShare.None)
            || bufferSize is <= NoBufferBytes or > MaximumBufferBytes)
        { throw new ArgumentOutOfRangeException(nameof(access)); }
    }

    private static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains(NullPathCharacter, StringComparison.Ordinal))
        { throw OfflineNativeErrors.InvalidEntry(); }
        if (!BitConverter.IsLittleEndian || !IsSupportedArchitecture()
            || !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        { throw OfflineNativeErrors.Unsupported(); }
    }

    private static bool IsSupportedArchitecture() => RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64;

    private const int LinuxNonBlocking = 0x800;
    private const int MacNonBlocking = 4;
}
