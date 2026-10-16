using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

/// <summary>Borrowed private observation of the actual local native forward owner, never a replacement work or authority.</summary>
internal interface IReplicaForwardedCommandObservation
{
    void Created(IReplicaForwardedCommandWork work);
    ValueTask BeforeExecuteAsync(ReplicatedOperation operation, IReplicaForwardedCommandWork work,
        CancellationToken cancellationToken);
}
