using Microsoft.Win32.SafeHandles;

namespace KeyLoad.Storage.IO;

internal sealed class OfflineNativeHandleLease(SafeFileHandle handle) : IDisposable
{
    private const int NotDisposed = 0;
    private const int Disposed = 1;
    private readonly SafeFileHandle ownedHandle = handle;
    private Exception? primaryFailure;
    private bool lockAcquired;
    private bool ownershipTransferred;
    private int disposalState;

    internal SafeFileHandle Handle => !ownershipTransferred && Volatile.Read(ref disposalState) == NotDisposed
        ? ownedHandle : throw new ObjectDisposedException(nameof(OfflineNativeHandleLease));

    internal void AcquireLock(FileShare share)
    {
        OfflineFileLock.Acquire(Handle, share);
        lockAcquired = true;
    }

    internal FileStream TransferToStream(FileAccess access, int bufferSize)
    {
        var stream = OfflineLockedFileStream.Create(Handle, access, bufferSize);
        ownershipTransferred = true;
        lockAcquired = false;
        return stream;
    }

    internal FileStream TransferToReadOnlyStream(int bufferSize)
    {
        var stream = new FileStream(Handle, FileAccess.Read, bufferSize, isAsync: false);
        ownershipTransferred = true;
        return stream;
    }

    internal void RecordPrimary(Exception error) => primaryFailure ??= error;

    internal static void DisposeUntransferred(ref SafeFileHandle? handle, Exception? primaryFailure)
    {
        var owned = handle;
        handle = null;
        try
        { owned?.Dispose(); }
        catch (Exception cleanup) when (primaryFailure is not null)
        { throw new AggregateException(primaryFailure, cleanup); }
    }

    public void Dispose()
    {
        if (ownershipTransferred || Interlocked.Exchange(ref disposalState, Disposed) == Disposed)
        { return; }
        var releaseLock = lockAcquired;
        lockAcquired = false;
        var failure = primaryFailure;
        try
        { OfflineFileLock.Release(ownedHandle, releaseLock); }
        catch (Exception cleanup)
        {
            if (failure is null)
            {
                failure = cleanup;
                throw;
            }
            failure = new AggregateException(failure, cleanup);
            throw failure;
        }
        finally
        { DisposeOwnedHandle(failure); }
    }

    private void DisposeOwnedHandle(Exception? failure)
    {
        try
        { ownedHandle.Dispose(); }
        catch (Exception cleanup) when (failure is not null)
        { throw new AggregateException(failure, cleanup); }
    }
}
