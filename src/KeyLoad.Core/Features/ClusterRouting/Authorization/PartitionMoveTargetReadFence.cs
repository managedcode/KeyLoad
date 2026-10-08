using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireNoUnpublishedPartitionMoveTarget(IKeyValueView view, PartitionRef partition)
    {
        if (configuredPhysicalOwner is null)
        { RequireUnconfiguredMovementAbsent(view); return; }
        var stage = PartitionMoveTargetStorage.Read<PartitionMoveTargetStage>(view,
            PartitionMoveTargetStorage.Key(partition), Limits.MaxBatchBytes);
        if (stage is not null && (view is not Features.ClusterRouting.Execution.PartitionControlResourceView controlled
            || controlled.Partition != partition || !stage.Published))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.Fenced); }
    }
}
