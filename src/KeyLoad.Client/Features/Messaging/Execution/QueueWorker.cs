using Microsoft.Extensions.Options;

namespace KeyLoad.Client;

/// <summary>Runs bounded external handlers over native persisted queue leases.</summary>
public static class QueueWorker
{
    /// <summary>Runs one finite auto-ACK invocation; external effects remain application-idempotent.</summary>
    /// <param name="client">Original authenticated caller-owned client, never disposed here.</param>
    /// <param name="lane">Selected native queue lane.</param>
    /// <param name="originalReceiveId">Original first receive ID, retained after uncertainty.</param>
    /// <param name="handler">Application callback outside server storage gates; cancellation is cooperative.</param>
    /// <param name="options">Validated caller policy, snapshotted before first await.</param>
    /// <param name="clock">Original caller composition clock.</param>
    /// <param name="cancellationToken">Cancellation followed by joined owned settlement.</param>
    /// <returns>Bounded original identities and actual native outcomes after every owned task joins.</returns>
    public static Task<QueueWorkerResult> RunQueueAckWorkerAsync(this KeyLoadClient client, QueueLaneRef lane,
        Guid originalReceiveId, Func<QueueWorkerDeliveryContext, CancellationToken, Task> handler,
        IOptions<QueueWorkerOptions> options, TimeProvider clock, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(lane);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(clock);
        if (originalReceiveId == Guid.Empty)
        {
            throw new ArgumentException(QueueWorkerProtocol.InvalidReceiveId, nameof(originalReceiveId));
        }
        var snapshot = QueueWorkerPolicy.Snapshot(options);
        return QueueWorkerExecution.RunAsync(client, lane, originalReceiveId, handler, snapshot, clock, cancellationToken);
    }
}
