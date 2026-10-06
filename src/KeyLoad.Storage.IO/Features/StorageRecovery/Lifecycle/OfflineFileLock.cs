using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace KeyLoad.Storage.IO;

internal static class OfflineFileLock
{
    private const int NativeCallSuccess = 0;
    private const int LockShared = 1;
    private const int LockExclusive = 2;
    private const int LockNonBlocking = 4;
    private const int LockUnlock = 8;

    internal static void Acquire(SafeFileHandle handle, FileShare share) => Apply(handle,
        (share == FileShare.Read ? LockShared : LockExclusive) | LockNonBlocking);

    internal static void Close(SafeFileHandle? handle, bool releaseLock, Exception? primaryFailure)
    {
        if (handle is null)
        { return; }
        try
        { Release(handle, releaseLock); }
        catch (Exception cleanup)
        {
            if (primaryFailure is null)
            {
                primaryFailure = cleanup;
                throw;
            }
            primaryFailure = new AggregateException(primaryFailure, cleanup);
            throw primaryFailure;
        }
        finally
        { OfflineNativeHandleLease.DisposeUntransferred(ref handle, primaryFailure); }
    }

    internal static void Release(SafeFileHandle handle, bool releaseLock)
    {
        if (releaseLock && !handle.IsClosed)
        { Apply(handle, LockUnlock); }
    }

    private static void Apply(SafeFileHandle handle, int operation)
    {
        var isMac = OperatingSystem.IsMacOS();
        var descriptor = handle.DangerousGetHandle().ToInt32();
        int result;
        try
        { result = isMac ? OfflineMacFileSystem.Lock(descriptor, operation) : OfflineLinuxFileSystem.Lock(descriptor, operation); }
        catch (Exception error) when (OfflineNativeErrors.IsUnavailable(error))
        { throw OfflineNativeErrors.Unsupported(); }
        if (result != NativeCallSuccess)
        { throw OfflineNativeErrors.FromErrno(Marshal.GetLastPInvokeError(), isMac); }
    }
}
