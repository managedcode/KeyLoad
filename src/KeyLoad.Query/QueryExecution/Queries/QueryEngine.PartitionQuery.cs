using KeyLoad.Query.Features.QueryExecution;

namespace KeyLoad.Query;

public sealed partial class QueryEngine
{
    /// <summary>Executes a complete authorized query over bounded partitions on this owner.</summary>
    /// <param name="principalId">Persisted database principal identifier.</param>
    /// <param name="request">The public typed partition-query request.</param>
    /// <param name="expectedOwner">The immutable server-owned physical owner tuple.</param>
    /// <param name="timeProvider">The clock for the operation's single read budget.</param>
    /// <param name="cancellationToken">Cancellation for admission, reads, merge and sizing.</param>
    /// <returns>The complete typed result and scoped read witnesses.</returns>
    public PartitionQueryPageV1 QueryPartitions(string principalId,
        PartitionQueryRequestV1 request, PhysicalShardRecord expectedOwner,
        TimeProvider? timeProvider = null, CancellationToken cancellationToken = default)
        => PartitionQueryPublicExecution.Execute(this, principalId, request, expectedOwner,
            timeProvider, cancellationToken);
}
