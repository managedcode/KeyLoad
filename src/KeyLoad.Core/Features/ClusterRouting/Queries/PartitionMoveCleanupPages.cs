using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private byte[] MoveCleanupPagePrefix(IKeyValueView view, PartitionMovePhaseCommand phase,
        PartitionMoveCleanupState state)
    {
        var stage = PartitionMoveTargetStorage.Read<PartitionMoveTargetStage>(view,
            PartitionMoveTargetStorage.Key(phase.Partition), Limits.MaxBatchBytes);
        if (stage is null)
        { return KeySpace.Partition(PartitionMoveProtocol.TargetPageSpace, phase.Partition, phase.MoveId); }
        if (stage.Control.Partition != phase.Partition || state.Role == PartitionMoveCleanupRole.Target
                && (stage.Control.MoveId != phase.MoveId || stage.Published)
            || state.Role == PartitionMoveCleanupRole.Source && !stage.Published)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
        return KeySpace.Partition(PartitionMoveProtocol.TargetPageSpace, phase.Partition, stage.Control.MoveId);
    }

    private void RemoveRetiredMoveFence(IAtomicTransaction transaction, PartitionRef partition, Guid moveId)
    {
        var fence = PartitionMoveSourceFenceStorage.Read(transaction, partition, Limits.MaxBatchBytes);
        if (fence is null)
        { return; }
        var retired = PartitionMoveCleanupStorage.Read(transaction, partition, fence.MoveId, Limits.MaxBatchBytes);
        if (fence.MoveId == moveId || retired is null || retired.Stage != PartitionMovePeerStage.Retire
            || retired.Role != PartitionMoveCleanupRole.Source || retired.Completion is null
            || retired.ControlIntentDigest != fence.ControlIntentDigest)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.Fenced); }
        transaction.Delete(PartitionMoveSourceFenceStorage.Key(partition));
    }
}
