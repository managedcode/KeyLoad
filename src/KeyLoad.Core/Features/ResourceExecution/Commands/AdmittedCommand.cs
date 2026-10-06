namespace KeyLoad.Core;

/// <summary>Owns one dispatched operation and its completion until the consumer reports an outcome.</summary>
public sealed class AdmittedCommand : IDisposable
{
    private const int SingleElementCount = 1;
    private const int EqualOrder = 0;

    private readonly TaskCompletionSource<OperationResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CommandAdmissionLease? lease;
    private int finalized;

    /// <summary>Gets the admitted immutable operation.</summary>
    public ReplicatedOperation Operation { get; }

    /// <summary>Gets the task completed when the consumer records the operation outcome.</summary>
    public Task<OperationResult> Completion => completion.Task;

    internal AdmittedCommand(ReplicatedOperation operation, CommandAdmissionLease lease)
    {
        Operation = operation;
        this.lease = lease;
    }

    /// <summary>Completes the command and releases its reservation exactly once.</summary>
    /// <param name="result">The operation result returned to the submitting caller.</param>
    public void Complete(OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!TryFinalize())
        {
            return;
        }

        completion.TrySetResult(result);
    }

    /// <summary>Fails the command and releases its reservation exactly once.</summary>
    /// <param name="exception">The failure returned to the submitting caller.</param>
    public void Fail(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (!TryFinalize())
        {
            return;
        }

        completion.TrySetException(exception);
    }

    /// <summary>Reports an unknown outcome when disposed before completion and releases its reservation once.</summary>
    public void Dispose()
    {
        if (!TryFinalize())
        {
            return;
        }

        completion.TrySetException(Errors.Fail(ErrorCode.UnknownWriteOutcome, CommandAdmissionDetails.UnknownWriteOutcome));
    }

    private bool TryFinalize()
    {
        if (Interlocked.CompareExchange(ref finalized, SingleElementCount, EqualOrder) != EqualOrder)
        {
            return false;
        }

        Interlocked.Exchange(ref lease, null)?.Dispose();
        return true;
    }
}
