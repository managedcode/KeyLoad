namespace KeyLoad.Core;

/// <summary>Owns one dispatched operation and its completion until the consumer reports an outcome.</summary>
public sealed class AdmittedCommand : IDisposable
{
    private const int SingleElementCount = 1;
    private const int EqualOrder = 0;
    private const string QueueOwnsCommand = "The command remains owned by its admission queue.";

    private readonly TaskCompletionSource<OperationResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CommandAdmissionLease? lease;
    private int finalized;
    private int dequeued;

    /// <summary>Gets the admitted immutable operation.</summary>
    public ReplicatedOperation Operation { get; }

    internal bool Control { get; }

    internal string Tenant { get; }

    /// <summary>Gets the task completed when the consumer records the operation outcome.</summary>
    public Task<OperationResult> Completion => completion.Task;

    internal AdmittedCommand(ReplicatedOperation operation, CommandAdmissionLease lease)
    {
        Operation = operation;
        Control = lease.Control;
        Tenant = lease.Tenant;
        this.lease = lease;
    }

    internal void TransferFromQueue() => Volatile.Write(ref dequeued, SingleElementCount);

    /// <summary>Completes the dequeued command and releases its reservation exactly once.</summary>
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

    /// <summary>Fails the dequeued command and releases its reservation exactly once.</summary>
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

    /// <summary>Retains queue-owned work; reports an unknown outcome for an unresolved dequeued command and releases once.</summary>
    public void Dispose()
    {
        if (Volatile.Read(ref dequeued) == EqualOrder)
        {
            return;
        }
        if (!TryFinalize())
        {
            return;
        }

        completion.TrySetException(Errors.Fail(ErrorCode.UnknownWriteOutcome, CommandAdmissionDetails.UnknownWriteOutcome));
    }

    private bool TryFinalize()
    {
        if (Volatile.Read(ref dequeued) == EqualOrder)
        {
            throw new InvalidOperationException(QueueOwnsCommand);
        }
        if (Interlocked.CompareExchange(ref finalized, SingleElementCount, EqualOrder) != EqualOrder)
        {
            return false;
        }

        Interlocked.Exchange(ref lease, null)?.Dispose();
        return true;
    }
}
