using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Clustering;

/// <summary>One submitted command waiting in its database's commit queue until a batch decides it.</summary>
/// <remarks>A caller that cancels before its command joins a batch sees cancellation; once the command was sent to
/// the copies its outcome is uncertain, so the caller receives <see cref="ErrorCode.UnknownWriteOutcome"/>.</remarks>
internal sealed class StorageCommitRequest
{
    private const int Queued = 0;
    private const int Sent = 1;
    private const int Cancelled = 2;
    private readonly TaskCompletionSource<Result<object>> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationToken cancellationToken;
    private readonly CancellationTokenRegistration registration;
    private int state;

    internal StorageCommitRequest(Command<DatabaseCommand> operation, CancellationToken cancellationToken)
    {
        Operation = operation;
        this.cancellationToken = cancellationToken;
        registration = cancellationToken.Register(static request => ((StorageCommitRequest)request!).Cancel(), this);
    }

    internal Command<DatabaseCommand> Operation
    {
        get;
    }

    internal Task<Result<object>> Completion => completion.Task;

    /// <summary>Claims a queued command for the next batch; false when its caller already cancelled it.</summary>
    internal bool TrySend() => Interlocked.CompareExchange(ref state, Sent, Queued) == Queued;

    /// <summary>Returns a command no copy applied to the queue; false when its caller cancelled it.</summary>
    internal bool TryReturn() => Interlocked.CompareExchange(ref state, Queued, Sent) == Sent;

    internal void Complete(StorageCopyOutcome outcome)
    {
        if (outcome.Failure is { } failure)
        {
            completion.TrySetException(failure);
        }
        else
        {
            completion.TrySetResult(outcome.Result);
        }
        Release();
    }

    /// <summary>Stops observing the caller's cancellation once the command is decided or abandoned.</summary>
    internal void Release() => registration.Dispose();

    private void Cancel()
    {
        if (Interlocked.Exchange(ref state, Cancelled) == Queued)
        {
            completion.TrySetCanceled(cancellationToken);
            return;
        }

        completion.TrySetException(Errors.Fail(ErrorCode.UnknownWriteOutcome, StorageCopyContract.WriteUncertain,
            new OperationCanceledException(cancellationToken)));
    }
}
