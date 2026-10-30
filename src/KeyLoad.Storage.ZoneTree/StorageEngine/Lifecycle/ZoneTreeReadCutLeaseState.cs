using ZoneTree.Transactional;

namespace KeyLoad.Storage.ZoneTree;

internal sealed class ZoneTreeReadCutLeaseState
{
    private const int NoTraversalThreadId = 0;
    private const int NoFailures = 0;
    private const int SingleFailureCount = 1;
    private const int FirstFailureIndex = 0;

    private const string ClosedMessage = "The execution read-cut lease is closed.";
    private const string ConcurrentTraversalMessage = "A execution read-cut lease permits only one active traversal.";
    private const string ReentrantDisposalMessage = "A execution read-cut lease cannot be disposed from its visitor.";
    private readonly Lock sync = new();
    private readonly TaskCompletionSource captureFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ZoneTreeSnapshotCursor? snapshot;
    private ZoneTreeReadCut? cut;
    private TaskCompletionSource? traversalFinished;
    private TaskCompletionSource? disposeFinished;
    private Exception? operationFailure;
    private int traversalThread;
    private bool traversing;
    private bool closing;
    private bool disposing;
    private bool finalized;

    internal ZoneTreeReadCut Cut
    {
        get
        {
            lock (sync)
            {
                return cut ?? throw Errors.Fail(ErrorCode.OwnershipLost, ClosedMessage);
            }
        }
    }

    internal void Attach(ZoneTreeSnapshotCursor acquired, ZoneTreeReadCut snapshotCut)
    {
        lock (sync)
        {
            snapshot = acquired;
            cut = snapshotCut;
        }
    }

    internal void FinishCapture() => captureFinished.TrySetResult();

    internal ITransactionIterator<Memory<byte>, Memory<byte>> BeginTraversal()
    {
        lock (sync)
        {
            if (closing || disposing || cut is null || snapshot is null)
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
            return snapshot.Iterator;
        }
    }

    internal void FinishTraversal()
    {
        TaskCompletionSource? finished;
        lock (sync)
        {
            traversing = false;
            traversalThread = NoTraversalThreadId;
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

    internal void EnsureTraversalSettled()
    {
        lock (sync)
        {
            if (traversing || !captureFinished.Task.IsCompleted)
            {
                throw new InvalidOperationException(ConcurrentTraversalMessage);
            }
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

    internal ZoneTreeSnapshotCursor? CurrentSnapshot()
    {
        lock (sync)
        {
            return snapshot;
        }
    }

    internal bool MarkSnapshotDisposed(ZoneTreeSnapshotCursor acquired)
    {
        lock (sync)
        {
            if (!ReferenceEquals(snapshot, acquired))
            {
                return false;
            }
            snapshot = null;
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

    internal Task CompleteDisposeAttempt(List<Exception> failures)
    {
        lock (sync)
        {
            var errors = new List<Exception>();
            if (failures.Count > NoFailures && operationFailure is not null)
            {
                errors.Add(operationFailure);
            }
            foreach (var cleanupFailure in failures)
            {
                AddUnique(errors, cleanupFailure);
            }
            var failure = errors.Count switch
            {
                NoFailures => null,
                SingleFailureCount => errors[FirstFailureIndex],
                _ => new AggregateException(errors)
            };

            var completion = disposeFinished!;
            if (failure is null)
            {
                completion.TrySetResult();
            }
            else
            {
                completion.TrySetException(failure);
            }
            disposing = false;
            return completion.Task;
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
