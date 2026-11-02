using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

/// <summary>One submitted command waiting in its database's commit queue until a batch decides it.</summary>
/// <remarks>A caller that cancels before its command joins a batch sees cancellation; once the command was sent to
/// the copies its outcome is uncertain, so the caller receives <see cref="ErrorCode.UnknownWriteOutcome"/>.</remarks>
internal sealed class StorageCommitRequest<TResult> : IStorageCommitRequest
{
    private const int Queued = 0;
    private const int Sent = 1;
    private const int Cancelled = 2;
    private readonly TaskCompletionSource<Result<TResult>> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationToken cancellationToken;
    private readonly CancellationTokenRegistration registration;
    private int state;

    internal StorageCommitRequest(Command<DatabaseCommand> operation, CancellationToken cancellationToken)
    {
        Operation = operation;
        this.cancellationToken = cancellationToken;
        registration = (cancellationToken.Register(static request => (((StorageCommitRequest<TResult>)request! /* KLD0042: The native cancellation registration restores its original StorageCommitRequest<TResult> state reference; the static callback avoids an extra delegate capture. */)).Cancel(), this) /* KLD0041: The native cancellation registration takes object state; retain the typed request instance in its static callback without allocating a capture delegate. */);
    }

    public Command<DatabaseCommand> Operation
    {
        get;
    }

    internal Task<Result<TResult>> Completion => completion.Task;

    /// <summary>Claims a queued command for the next batch; false when its caller already cancelled it.</summary>
    public bool TrySend() => Interlocked.CompareExchange(ref state, Sent, Queued) == Queued;

    /// <summary>Returns a command no copy applied to the queue; false when its caller cancelled it.</summary>
    public bool TryReturn() => Interlocked.CompareExchange(ref state, Queued, Sent) == Sent;

    public void Complete(StorageCopyOutcome outcome)
    {
        try
        {
            if (outcome.Failure is { } failure)
            {
                completion.TrySetException(failure);
            }
            else
            {
                var /* KLD0041: Read the current mixed persisted outcome only at typed queue completion, then publish the concrete Result<TResult>. */ result = (outcome.Result /* KLD0041: Read the current mixed persisted outcome only at typed queue completion, then publish the concrete Result<TResult>. */);
                completion.TrySetResult(result.IsFailed ? Result<TResult>.Fail(result.Problem)
                    : Result<TResult>.Succeed((result.Get<TResult>() /* KLD0041: Read the current mixed persisted outcome only at typed queue completion, then publish the concrete Result<TResult>. */)));
            }
        }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null)
        {
            completion.TrySetException(error);
        }
        finally
        {
            Release();
        }
    }

    /// <summary>Stops observing the caller's cancellation once the command is decided or abandoned.</summary>
    public void Release() => registration.Dispose();

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
