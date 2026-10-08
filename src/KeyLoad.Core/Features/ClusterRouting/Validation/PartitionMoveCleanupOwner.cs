using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Queries;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireMoveCleanupOwner(IKeyValueView view, PartitionMoveCleanupBody body,
        PartitionMovePhaseCommand phase)
    {
        if (body.Role == PartitionMoveCleanupRole.Source)
        {
            var fence = PartitionMoveSourceFenceStorage.Read(view, phase.Partition, Limits.MaxBatchBytes);
            if (fence is not null && (fence.MoveId != phase.MoveId
                || fence.ControlIntentDigest != phase.ControlIntentDigest))
            { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
            if (phase.Stage == PartitionMovePeerStage.Retire && fence is null)
            { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.Fenced); }
            return;
        }
        var target = PartitionMoveTargetStorage.Read<PartitionMoveTargetStage>(view,
            PartitionMoveTargetStorage.Key(phase.Partition), Limits.MaxBatchBytes);
        if (target is not null)
        {
            if (target.Control.MoveId != phase.MoveId || target.Published
                || JsonData.Fingerprint(target.Descriptor.Resources) != JsonData.Fingerprint(phase.Resources))
            { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
            return;
        }
        foreach (var family in PartitionRecordFamilies.All)
        {
            if (!PartitionMoveFamilyCapture.ControlOwned(family)
                && !view.Scan(KeySpace.Partition(family, phase.Partition), PartitionMoveProtocol.SequenceStep).Records.IsEmpty)
            { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        }
    }
}
