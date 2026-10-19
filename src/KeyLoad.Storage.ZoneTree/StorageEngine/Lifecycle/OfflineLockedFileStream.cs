using Microsoft.Win32.SafeHandles;

namespace KeyLoad.Storage.ZoneTree.IO;

internal sealed class OfflineLockedFileStream : FileStream
{
    private readonly string fileName;
    private readonly SafeFileHandle originalHandle;
    private readonly SafeFileHandle borrowedHandle;
    private readonly Lock disposalGate = new();
    private readonly AsyncLocal<bool> disposingBaseView = new();
    private Task? disposalCompletion;

    private OfflineLockedFileStream(SafeFileHandle original, SafeFileHandle borrowed, string path, FileAccess access, int bufferSize)
        : base(borrowed, access, bufferSize, isAsync: false)
    {
        fileName = path;
        originalHandle = original;
        borrowedHandle = borrowed;
    }

    internal static FileStream Create(SafeFileHandle original, string path, FileAccess access, int bufferSize)
    {
        var absolutePath = Path.GetFullPath(path);
        SafeFileHandle? borrowed = new(original.DangerousGetHandle(), ownsHandle: false);
        Exception? primaryFailure = null;
        try
        {
            var stream = new OfflineLockedFileStream(original, borrowed, absolutePath, access, bufferSize);
            borrowed = null;
            return stream;
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally
        {
            OfflineHandleLease.DisposeUntransferred(ref borrowed, primaryFailure);
        }
    }

    public override string Name => fileName;

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
            {
                borrowedHandle.Dispose();
            }
            base.Dispose(disposing);
            return;
        }
        BeginDispose(useAsync: false).GetAwaiter().GetResult();
    }

    private Task BeginDispose(bool useAsync, Func<ValueTask>? disposeAsyncBase = null)
    {
        if (!useAsync)
        {
            return DisposeSynchronousOwner();
        }
        TaskCompletionSource? start = null;
        Task completion;
        lock (disposalGate)
        {
            if (disposalCompletion is null)
            {
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                disposalCompletion = DisposeOwnerAsync(start.Task, disposeAsyncBase!);
            }
            completion = disposalCompletion;
        }
        start?.SetResult();
        return completion;
    }

    private async Task DisposeOwnerAsync(Task start, Func<ValueTask> disposeAsyncBase)
    {
        await start;
        Exception? primaryFailure = null;
        try
        {
            if (originalHandle.IsClosed)
            {
                borrowedHandle.Dispose();
            }
            else
            {
                await base.FlushAsync(CancellationToken.None);
            }
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally
        {
            await DisposeViewAndOwnerAsync(disposeAsyncBase, primaryFailure);
        }
    }

    private Task DisposeSynchronousOwner()
    {
        lock (disposalGate)
        {
            if (disposalCompletion is not null)
            {
                return disposalCompletion;
            }
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            disposalCompletion = completion.Task;
            OfflineSynchronousFileDisposal.Complete(completion, () =>
            {
                if (originalHandle.IsClosed)
                {
                    borrowedHandle.Dispose();
                }
                else
                {
                    FlushSynchronous();
                }
            }, DisposeViewAndOwner);
            return completion.Task;
        }
    }

    private void FlushSynchronous() => base.Flush();

    private void DisposeViewAndOwner(Exception? primaryFailure)
    {
        try
        {
            base.Dispose(disposing: true);
        }
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
            OfflineFileLock.Close(originalHandle, releaseLock: true, primaryFailure);
        }
    }

    private async Task DisposeViewAndOwnerAsync(Func<ValueTask> disposeViewAsync, Exception? primaryFailure)
    {
        var previousEntry = disposingBaseView.Value;
        disposingBaseView.Value = true;
        try
        {
            await disposeViewAsync();
        }
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
