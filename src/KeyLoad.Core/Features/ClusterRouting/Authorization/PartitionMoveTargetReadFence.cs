using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireNoUnpublishedPartitionMoveTarget(IKeyValueView view, PartitionRef partition)
    {
        var stage = PartitionMoveTargetStorage.Read<PartitionMoveTargetStage>(view,
            PartitionMoveTargetStorage.Key(partition), Limits.MaxBatchBytes);
        if (stage is not null)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.Fenced); }
    }
}
