using ZoneTree;

namespace KeyLoad.Storage.ZoneTree;

internal sealed class ZoneTreeReadCutLeaseState
{
    private const string ClosedMessage = "The native read-cut lease is closed.";
    private const string ConcurrentTraversalMessage = "A native read-cut lease permits only one active traversal.";
    private const string ReentrantDisposalMessage = "A native read-cut lease cannot be disposed from its visitor.";
    private readonly Lock sync = new();
    private readonly TaskCompletionSource captureFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IZoneTreeIterator<Memory<byte>, Memory<byte>>? iterator;
    private ZoneTreeNativeReadCut? cut;
    private TaskCompletionSource? traversalFinished;
    private TaskCompletionSource? disposeFinished;
    private Exception? operationFailure;
    private int traversalThread;
    private bool traversing;
    private bool closing;
    private bool disposing;
    private bool finalized;

    internal ZoneTreeNativeReadCut Cut
    {
        get
        {
            lock (sync)
            {
                return cut ?? throw Errors.Fail(ErrorCode.OwnershipLost, ClosedMessage);
            }
        }
    }

    internal void Attach(IZoneTreeIterator<Memory<byte>, Memory<byte>> acquired, ZoneTreeNativeReadCut snapshotCut)
    {
        lock (sync)
        {
            iterator = acquired;
            cut = snapshotCut;
        }
    }

    internal void FinishCapture() => captureFinished.TrySetResult();

    internal IZoneTreeIterator<Memory<byte>, Memory<byte>> BeginTraversal()
    {
        lock (sync)
        {
            if (closing || disposing || cut is null || iterator is null)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, ClosedMessage);
            }
            if (traversing)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, ConcurrentTraversalMessage);
            }

            traversing = true;
            traversalThread = Environment.CurrentManagedThreadId;
            traversalFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return iterator;
        }
    }

    internal void FinishTraversal()
    {
        TaskCompletionSource? finished;
        lock (sync)
        {
            traversing = false;
            traversalThread = 0;
            finished = traversalFinished;
        }
        finished?.TrySetResult();
    }

    internal void RecordOperationFailure(Exception failure)
    {
        lock (sync)
        {
            operationFailure ??= failure;
        }
    }

    internal void EnsureDisposalCanJoin()
    {
        lock (sync)
        {
            if (traversing && traversalThread == Environment.CurrentManagedThreadId)
            {
                throw new InvalidOperationException(ReentrantDisposalMessage);
            }
        }
    }

    internal (bool Owner, Task Capture, Task? Traversal, Task Completion) BeginDispose()
    {
        lock (sync)
        {
            if (traversing && traversalThread == Environment.CurrentManagedThreadId)
            {
                throw new InvalidOperationException(ReentrantDisposalMessage);
            }
            closing = true;
            if (disposing)
            {
                return (false, captureFinished.Task, traversalFinished?.Task, disposeFinished!.Task);
            }
            if (finalized)
            {
                return (false, captureFinished.Task, traversalFinished?.Task, Task.CompletedTask);
            }

            disposing = true;
            disposeFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return (true, captureFinished.Task, traversalFinished?.Task, disposeFinished.Task);
        }
    }

    internal IZoneTreeIterator<Memory<byte>, Memory<byte>>? CurrentIterator()
    {
        lock (sync)
        {
            return iterator;
        }
    }

    internal bool MarkIteratorDisposed(IZoneTreeIterator<Memory<byte>, Memory<byte>> acquired)
    {
        lock (sync)
        {
            if (!ReferenceEquals(iterator, acquired))
            {
                return false;
            }
            iterator = null;
            cut = null;
            return true;
        }
    }

    internal void MarkFinalized()
    {
        lock (sync)
        {
            finalized = true;
        }
    }

    internal Exception? CompleteDisposeAttempt(List<Exception> failures)
    {
        lock (sync)
        {
            var errors = new List<Exception>();
            if (failures.Count > 0 && operationFailure is not null)
            {
                errors.Add(operationFailure);
            }
            foreach (var cleanupFailure in failures)
            {
                AddUnique(errors, cleanupFailure);
            }
            var failure = errors.Count switch
            {
                0 => null,
                1 => errors[0],
                _ => new AggregateException(errors)
            };

            if (failure is null)
            {
                disposeFinished!.TrySetResult();
            }
            else
            {
                disposeFinished!.TrySetException(failure);
            }
            disposing = false;
            return failure;
        }
    }

    private static void AddUnique(List<Exception> errors, Exception failure)
    {
        foreach (var existing in errors)
        {
            if (ReferenceEquals(existing, failure))
            {
                return;
            }
        }
        errors.Add(failure);
    }
}
