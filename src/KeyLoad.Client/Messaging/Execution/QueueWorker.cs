using Microsoft.Extensions.Options;

namespace KeyLoad.Client;

/// <summary>Runs bounded external handlers over execution persisted queue leases.</summary>
public static class QueueWorker
{
    /// <summary>Runs one finite auto-ACK invocation; external effects remain application-idempotent.</summary>
    /// <param name="client">Original authenticated caller-owned client, never disposed here.</param>
    /// <param name="lane">Selected execution queue lane.</param>
    /// <param name="originalReceiveId">Original first receive ID, retained after uncertainty.</param>
    /// <param name="handler">Application callback outside server storage gates; cancellation is cooperative.</param>
    /// <param name="options">Validated caller policy, snapshotted before first await.</param>
    /// <param name="clock">Original caller composition clock.</param>
    /// <param name="cancellationToken">Cancellation followed by joined owned settlement.</param>
    /// <returns>Bounded original identities and actual execution outcomes after every owned task joins.</returns>
    public static Task<QueueWorkerResult> RunQueueAckWorkerAsync(this KeyLoadClient client, QueueLaneRef lane,
        Guid originalReceiveId, Func<QueueWorkerDeliveryContext, CancellationToken, Task> handler,
        IOptions<QueueWorkerOptions> options, TimeProvider clock, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client) /* KLD0041: BCL null validation accepts the client KeyLoadClient reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(lane) /* KLD0041: BCL null validation accepts the lane QueueLaneRef reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(handler) /* KLD0041: BCL null validation accepts the handler Func reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(clock) /* KLD0041: BCL null validation accepts the clock TimeProvider reference through its object parameter. */;
        if (originalReceiveId == Guid.Empty)
        {
            throw new ArgumentException(QueueWorkerProtocol.InvalidReceiveId, nameof(originalReceiveId));
        }
        var snapshot = QueueWorkerPolicy.Snapshot(options);
        return QueueWorkerExecution.RunAsync(client, lane, originalReceiveId, handler, snapshot, clock, cancellationToken);
    }
}
