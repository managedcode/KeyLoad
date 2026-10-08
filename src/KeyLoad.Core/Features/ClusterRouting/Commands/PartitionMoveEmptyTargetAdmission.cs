using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Queries;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionMoveTargetStage RequireMoveInstallStage(IAtomicTransaction transaction,
        PartitionMoveInstallBody body)
    {
        var existing = PartitionMoveTargetStorage.Read<PartitionMoveTargetStage>(transaction,
            PartitionMoveTargetStorage.Key(body.Control.Partition), Limits.MaxBatchBytes);
        if (existing is not null)
        { return existing; }
        if (body.Descriptor.Families.Any(value => value.PageCount != PartitionMoveProtocol.EmptyCount))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        foreach (var family in PartitionRecordFamilies.All)
        {
            if (!PartitionMoveFamilyCapture.ControlOwned(family)
                && transaction.Scan(KeySpace.Partition(family, body.Control.Partition),
                    PartitionMoveProtocol.SequenceStep).Records.Length != PartitionMoveProtocol.EmptyCount)
            { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        }
        return new(PartitionMoveProtocol.Version, body.Control, body.Fence, body.Descriptor,
            PartitionMoveProtocol.EmptyCount, PartitionMoveProtocol.EmptyCount, false, false);
    }
}
