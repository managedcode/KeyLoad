using Microsoft.Win32.SafeHandles;

namespace KeyLoad.Storage.IO;

internal sealed class OfflineLockedFileStream : FileStream
{
    private readonly SafeFileHandle originalHandle;
    private readonly SafeFileHandle borrowedHandle;
    private readonly Lock disposalGate = new();
    private readonly AsyncLocal<bool> disposingBaseView = new();
    private Task? disposalCompletion;

    private OfflineLockedFileStream(SafeFileHandle original, SafeFileHandle borrowed, FileAccess access, int bufferSize)
        : base(borrowed, access, bufferSize, isAsync: false)
    {
        originalHandle = original;
        borrowedHandle = borrowed;
    }

    internal static FileStream Create(SafeFileHandle original, FileAccess access, int bufferSize)
    {
        SafeFileHandle? borrowed = new(original.DangerousGetHandle(), ownsHandle: false);
        Exception? primaryFailure = null;
        try
        {
            var stream = new OfflineLockedFileStream(original, borrowed, access, bufferSize);
            borrowed = null;
            return stream;
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally
        { OfflineNativeHandleLease.DisposeUntransferred(ref borrowed, primaryFailure); }
    }

    public override SafeFileHandle SafeFileHandle
    {
        get
        {
            _ = base.SafeFileHandle;
            return originalHandle;
        }
    }

    public override ValueTask DisposeAsync()
    {
        var completion = BeginDispose(useAsync: true, () => base.DisposeAsync());
        GC.SuppressFinalize(this);
        return new(completion);
    }

    protected override void Dispose(bool disposing)
    {
        // FileStream's derived async strategy reenters Dispose(bool) through Stream.Dispose.
        // Only that lexical cleanup entry bypasses the shared completion it is already running.
        if (disposing && disposingBaseView.Value)
        {
            base.Dispose(disposing);
            return;
        }
        if (!disposing)
        {
            if (originalHandle?.IsClosed == true)
            { borrowedHandle.Dispose(); }
            base.Dispose(disposing);
            return;
        }
        BeginDispose(useAsync: false).GetAwaiter().GetResult();
    }

    private Task BeginDispose(bool useAsync, Func<ValueTask>? disposeAsyncBase = null)
    {
        TaskCompletionSource? start = null;
        Task completion;
        lock (disposalGate)
        {
            if (disposalCompletion is null)
            {
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                disposalCompletion = DisposeOwnerAsync(useAsync, start.Task, disposeAsyncBase);
            }
            completion = disposalCompletion;
        }
        start?.SetResult();
        return completion;
    }

    private async Task DisposeOwnerAsync(bool useAsync, Task start, Func<ValueTask>? disposeAsyncBase)
    {
        await start.ConfigureAwait(false);
        Exception? primaryFailure = null;
        try
        {
            if (originalHandle.IsClosed)
            { borrowedHandle.Dispose(); }
            else if (useAsync)
            { await base.FlushAsync(CancellationToken.None).ConfigureAwait(false); }
            else
            { FlushSynchronous(); }
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally
        {
            if (useAsync)
            { await DisposeViewAndOwnerAsync(disposeAsyncBase!, primaryFailure).ConfigureAwait(false); }
            else
            { DisposeViewAndOwner(primaryFailure); }
        }
    }

    private void FlushSynchronous() => base.Flush();

    private void DisposeViewAndOwner(Exception? primaryFailure)
    {
        try
        { base.Dispose(disposing: true); }
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
        { OfflineFileLock.Close(originalHandle, releaseLock: true, primaryFailure); }
    }

    private async Task DisposeViewAndOwnerAsync(Func<ValueTask> disposeViewAsync, Exception? primaryFailure)
    {
        var previousEntry = disposingBaseView.Value;
        disposingBaseView.Value = true;
        try
        { await disposeViewAsync().ConfigureAwait(false); }
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
        {
            disposingBaseView.Value = previousEntry;
            OfflineFileLock.Close(originalHandle, releaseLock: true, primaryFailure);
        }
    }
}
