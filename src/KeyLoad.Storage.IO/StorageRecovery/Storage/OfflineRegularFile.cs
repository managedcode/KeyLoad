using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace KeyLoad.Storage.IO;

internal static class OfflineRegularFile
{
    private const int FirstValidDescriptor = 0;
    private const int NoBufferBytes = 0;
    private const char NullPathCharacter = '\0';
    private const uint RegularType = 0x8000;
    private const int MaximumBufferBytes = 65536;

    internal static void RequireRegular(string path) => _ = Inspect(path);

    internal static OfflineFileIdentity Inspect(string path)
    {
        ValidatePath(path);
        var metadata = InspectNativePath(path);
        if (metadata.Type != RegularType)
        {
            throw OfflineNativeErrors.InvalidEntry();
        }
        return metadata.Identity;
    }

    internal static FileStream Open(string path, FileAccess access, FileShare share, int bufferSize)
    {
        var expected = Inspect(path);
        return OpenWithIdentity(path, expected, access, share, bufferSize);
    }

    /// <summary>Opens a validated regular file for observation without taking or releasing its owner's flock.</summary>
    /// <remarks>The caller must control writers; this read-only handle does not provide a coherent snapshot against concurrent writes.</remarks>
    internal static FileStream OpenReadOnlyObservation(string path, int bufferSize)
    {
        ValidatePath(path);
        if (bufferSize is <= NoBufferBytes or > MaximumBufferBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferSize));
        }

        var expected = Inspect(path);
        using var lease = OpenNative(path, FileAccess.Read);
        try
        {
            VerifyDescriptor(lease.Handle, expected);
            VerifyPath(path, expected);
            return lease.TransferToReadOnlyStream(bufferSize);
        }
        catch (Exception error)
        {
            lease.RecordPrimary(error);
            throw;
        }
    }

    internal static FileStream OpenWithIdentity(string path, OfflineFileIdentity expected,
        FileAccess access, FileShare share, int bufferSize)
        => OpenWithIdentityCore(path, expected, access, share, bufferSize, writeThrough: false);

    internal static FileStream OpenWriteThrough(string path, FileShare share, int bufferSize)
        => OpenWriteThrough(path, Inspect(path), share, bufferSize);

    internal static FileStream OpenWriteThrough(string path, OfflineFileIdentity expected,
        FileShare share, int bufferSize)
        => OpenWithIdentityCore(path, expected, FileAccess.ReadWrite, share, bufferSize, writeThrough: true);

    private static FileStream OpenWithIdentityCore(string path, OfflineFileIdentity expected,
        FileAccess access, FileShare share, int bufferSize, bool writeThrough)
    {
        ValidateArguments(path, access, share, bufferSize);
        path = Path.GetFullPath(path);

        using var lease = OpenNative(path, access, writeThrough);
        try
        {
            VerifyDescriptor(lease.Handle, expected);
            lease.AcquireLock(share);
            VerifyPath(path, expected);
            return lease.TransferToStream(path, access, bufferSize);
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
        {
            throw OfflineNativeErrors.InvalidEntry();
        }
        if (metadata.Identity != expected)
        {
            throw OfflineNativeErrors.ChangedEntry();
        }
    }

    private static void VerifyPath(string path, OfflineFileIdentity expected)
    {
        var metadata = InspectNativePath(path);
        if (metadata.Type != RegularType)
        {
            throw OfflineNativeErrors.InvalidEntry();
        }
        if (metadata.Identity != expected)
        {
            throw OfflineNativeErrors.ChangedEntry();
        }
    }

    private static OfflineNativeHandleLease OpenNative(string path, FileAccess access, bool writeThrough = false)
    {
        var (flags, isMac) = OfflineRegularFileOpenFlags.Create(access, writeThrough);
        int descriptor;
        try
        {
            descriptor = isMac ? OfflineMacFileSystem.Open(path, flags) : OfflineLinuxFileSystem.Open(path, flags);
        }
        catch (Exception error) when (OfflineNativeErrors.IsUnavailable(error))
        {
            throw OfflineNativeErrors.Unsupported();
        }
        if (descriptor < FirstValidDescriptor)
        {
            throw OfflineNativeErrors.FromErrno(Marshal.GetLastPInvokeError(), isMac);
        }
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
        {
            OfflineNativeHandleLease.DisposeUntransferred(ref handle, primaryFailure);
        }
    }

    private static OfflineFileMetadata InspectNativePath(string path)
    {
        try
        {
            return OperatingSystem.IsMacOS() ? OfflineMacFileSystem.InspectPath(path) : OfflineLinuxFileSystem.InspectPath(path);
        }
        catch (Exception error) when (OfflineNativeErrors.IsUnavailable(error))
        {
            throw OfflineNativeErrors.Unsupported();
        }
    }

    private static OfflineFileMetadata InspectNativeHandle(SafeFileHandle handle)
    {
        try
        {
            return OperatingSystem.IsMacOS() ? OfflineMacFileSystem.InspectHandle(handle) : OfflineLinuxFileSystem.InspectHandle(handle);
        }
        catch (Exception error) when (OfflineNativeErrors.IsUnavailable(error))
        {
            throw OfflineNativeErrors.Unsupported();
        }
    }

    private static void ValidateArguments(string path, FileAccess access, FileShare share, int bufferSize)
    {
        ValidatePath(path);
        if (access is not (FileAccess.Read or FileAccess.ReadWrite)
            || share is not (FileShare.Read or FileShare.None)
            || bufferSize is <= NoBufferBytes or > MaximumBufferBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(access));
        }
    }

    private static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains(NullPathCharacter, StringComparison.Ordinal))
        {
            throw OfflineNativeErrors.InvalidEntry();
        }
        if (!BitConverter.IsLittleEndian || !IsSupportedArchitecture()
            || !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            throw OfflineNativeErrors.Unsupported();
        }
    }

    private static bool IsSupportedArchitecture() => RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64;

}
