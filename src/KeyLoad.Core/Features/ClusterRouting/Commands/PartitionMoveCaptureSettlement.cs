using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionMovePhaseResult ExecuteMoveCaptureSettlement(IAtomicTransaction transaction,
        Guid commandId, PartitionMovePhaseCommand phase, long position)
    {
        var body = NativeSerialization.Deserialize<PartitionMoveCaptureRequest>(phase.Body.Span);
        PartitionMoveSourceFenceValidation.Require(body.Fence, phase.Partition);
        if (body.MaximumPageBytes <= PartitionMoveProtocol.EmptyCount
            || body.MaximumPageBytes > Limits.MaxBatchBytes
            || body.MaximumImageBytes <= PartitionMoveProtocol.EmptyCount
            || body.MaximumImageBytes > Limits.MaxQueryReadBytes
            || body.MaximumRecords <= PartitionMoveProtocol.EmptyCount
            || body.MaximumRecords > Limits.MaxScanRecords)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        var fence = PartitionMoveSourceFenceStorage.Read(transaction, phase.Partition, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (fence.MoveId != phase.MoveId || fence.SourceCut != body.Fence.SourceCut
            || fence.ControlIntentDigest != phase.ControlIntentDigest
            || body.Fence.ControlIntentDigest != phase.ControlIntentDigest
            || !PartitionMoveControlValidation.SameSource(fence.SourcePlacement, phase.SourcePlacement)
            || !PhysicalOwnerEntryValidation.SameOwner(fence.ControlOwner, phase.ControlOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(fence.DestinationOwner, phase.DestinationOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        return new(phase.MoveId, phase.Stage, MoveJournalReceipt(transaction, commandId, position,
            phase.ControlIntentDigest), null, fence, null, null);
    }
}
