using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static void RequirePlacementControlPartition(IKeyValueView view, PartitionRef partition,
        string tenantId, string databaseId, PhysicalShardRecord control)
    {
        ValidatePartition(partition);
        var current = ResolveRegisteredPlacement(view, partition, control);
        if (partition.TenantId != tenantId || partition.DatabaseId != databaseId
            || current.PhysicalShardId != control.PhysicalShardId || current.Incarnation != control.Incarnation
            || !current.VoterIds.SequenceEqual(control.VoterIds, StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.OwnershipLost, TenantPlacementPolicyProtocol.ScopeMismatch); }
    }
}
