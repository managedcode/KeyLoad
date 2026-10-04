using Microsoft.Win32.SafeHandles;

namespace KeyLoad.Storage.IO;

internal sealed class OfflineNativeHandleLease(SafeFileHandle handle) : IDisposable
{
    private SafeFileHandle? ownedHandle = handle;
    private Exception? primaryFailure;

    internal SafeFileHandle Handle => ownedHandle ?? throw new ObjectDisposedException(nameof(OfflineNativeHandleLease));

    internal FileStream TransferToStream(FileAccess access, int bufferSize)
    {
        var stream = new FileStream(Handle, access, bufferSize, isAsync: false);
        ownedHandle = null;
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
        try
        { ownedHandle?.Dispose(); }
        catch (Exception cleanup) when (primaryFailure is not null)
        { throw new AggregateException(primaryFailure, cleanup); }
        ownedHandle = null;
    }
}
