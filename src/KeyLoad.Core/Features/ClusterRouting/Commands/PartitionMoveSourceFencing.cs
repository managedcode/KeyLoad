using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionMovePhaseResult ExecuteMoveFence(IAtomicTransaction transaction,
        Guid commandId, PartitionMovePhaseCommand phase, long position)
    {
        var body = NativeSerialization.Deserialize<PartitionMoveControlBody>(phase.Body.Span);
        var record = body.Control;
        RequireMovePhaseIdentity(phase, record);
        if (phase.GrantId is null || body.OperatorPrincipalId != record.PrincipalId
            || phase.ControlIntentDigest != PartitionMoveIntentIdentity.Digest(record)
            || record.Phase != PartitionMovePhase.Prepared)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var placement = ReadPlacementWitness(transaction, phase.Partition);
        if (!PartitionMoveControlValidation.SameSource(placement, record.SourcePlacement))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        PartitionMoveSourceQuiescence.Require(transaction, phase.Partition, Limits);
        var existing = PartitionMoveSourceFenceStorage.Read(transaction, phase.Partition, Limits.MaxBatchBytes);
        if (existing is not null)
        {
            if (existing.MoveId != phase.MoveId || existing.ControlIntentDigest != phase.ControlIntentDigest)
            { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
            return new(phase.MoveId, phase.Stage, MoveJournalReceipt(transaction, commandId, position,
                phase.ControlIntentDigest), record, existing, null, null);
        }
        var fence = new PartitionMoveSourceFenceRecord(PartitionMoveProtocol.Version, phase.MoveId,
            phase.Partition, phase.ControlOwner, record.SourcePlacement, record.DestinationOwner,
            position, phase.ControlIntentDigest);
        PartitionMoveSourceFenceStorage.Write(transaction, fence, Limits.MaxBatchBytes);
        return new(phase.MoveId, phase.Stage, MoveJournalReceipt(transaction, commandId, position,
            phase.ControlIntentDigest), record, fence, null, null);
    }
}
