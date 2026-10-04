namespace KeyLoad.Core;

/// <summary>Queues bounded admitted commands in control and data lanes for one registered reader.</summary>
/// <remarks>Creates an inbox using the supplied command governor.</remarks>
/// <param name="governor">The governor that owns command reservations.</param>
public sealed class AdmittedCommandInbox(CommandAdmissionGovernor governor) : IAsyncDisposable
{
    private const string PrincipalMismatchDetail = "Command admission requires the verified principal identity.";
    private const string StoppedDetail = "The node command queue has stopped accepting operations.";
    private const string ConcurrentReaderDetail = "The command inbox supports only one active reader.";

    private readonly object gate = new();
    private readonly CommandInboxLanes lanes = new();
    private readonly SemaphoreSlim available = new(0);
    private readonly TaskCompletionSource disposalCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CommandAdmissionGovernor governor = governor ?? throw new ArgumentNullException(nameof(governor));
    private bool stopped;
    private bool readerRegistered;
    private bool disposeStarted;
    private bool disposed;
    private TaskCompletionSource? readerDrained;

    /// <summary>Reserves and queues an operation after validating its verified-principal identity.</summary>
    /// <param name="operation">The immutable operation to enqueue.</param>
    /// <param name="principal">The verified principal associated with the operation.</param>
    /// <param name="payloadBytes">The operation payload size in bytes.</param>
    /// <param name="cancellationToken">Cancels admission before the command is queued.</param>
    /// <returns>The admitted command whose outcome must be completed, failed, or disposed by its consumer.</returns>
    public AdmittedCommand Enqueue(ReplicatedOperation operation, PrincipalRecord principal, int payloadBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(principal);
        if (principal.Id != operation.PrincipalId)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, PrincipalMismatchDetail);
        }

        var lease = governor.Reserve(operation.Kind, principal, payloadBytes, operation.PayloadJson.Length, cancellationToken);
        try
        {
            lock (gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfUnavailable();
                if (stopped)
                {
                    throw Errors.Fail(ErrorCode.ResourceExhausted, StoppedDetail);
                }

                var command = new AdmittedCommand(operation, lease);
                lanes.Enqueue(command);
                available.Release();
                lease = null;
                return command;
            }
        }
        finally
        {
            lease?.Dispose();
        }
    }

    /// <summary>Reads the next command while maintaining one-reader ownership.</summary>
    /// <param name="cancellationToken">Cancels a pending read without consuming queued work.</param>
    /// <returns>The next admitted command, or null after the inbox stops.</returns>
    public async ValueTask<AdmittedCommand?> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!RegisterReader())
        {
            return null;
        }

        try
        {
            await available.WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (gate)
            {
                return lanes.Dequeue(stopped);
            }
        }
        finally
        {
            UnregisterReader();
        }
    }

    /// <summary>Stops admission, fails queued commands with unknown outcome, and wakes the registered reader.</summary>
    public void Stop()
    {
        lock (gate)
        {
            if (stopped || disposed)
            {
                return;
            }

            stopped = true;
            lanes.FailQueued();
            available.Release();
        }
    }

    /// <summary>Stops the inbox and asynchronously drains its active read before disposing its semaphore.</summary>
    public ValueTask DisposeAsync()
    {
        Stop();
        Task drain;
        var startDisposal = false;
        lock (gate)
        {
            if (disposed)
            {
                return ValueTask.CompletedTask;
            }

            if (!disposeStarted)
            {
                disposeStarted = true;
                if (readerRegistered)
                {
                    readerDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    drain = readerDrained.Task;
                }
                else
                {
                    drain = Task.CompletedTask;
                }
                startDisposal = true;
            }
            else
            {
                drain = Task.CompletedTask;
            }
        }

        if (startDisposal)
        {
            _ = DisposeAfterReaderAsync(drain);
        }

        return new(disposalCompletion.Task);
    }

    private bool RegisterReader()
    {
        lock (gate)
        {
            ThrowIfUnavailable();
            if (stopped)
            {
                return false;
            }

            if (readerRegistered)
            {
                throw new InvalidOperationException(ConcurrentReaderDetail);
            }

            readerRegistered = true;
            return true;
        }
    }

    private void UnregisterReader()
    {
        lock (gate)
        {
            readerRegistered = false;
            readerDrained?.TrySetResult();
            readerDrained = null;
        }
    }

    private async Task DisposeAfterReaderAsync(Task drain)
    {
        await drain.ConfigureAwait(false);
        lock (gate)
        {
            if (!disposed)
            {
                available.Dispose();
                disposed = true;
            }
        }
        disposalCompletion.TrySetResult();
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(disposed || disposeStarted, this);
    }

}
