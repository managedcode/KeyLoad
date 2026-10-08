using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionMovePhaseResult ExecuteMoveCancelGrants(IAtomicTransaction transaction,
        PrincipalRecord principal, Guid commandId, PartitionMovePhaseCommand phase, long position)
    {
        var body = NativeSerialization.Deserialize<PartitionMoveCompletionBody>(phase.Body.Span);
        var current = PartitionMoveControlStorage.ReadHistory(transaction, phase.Partition,
            phase.MoveId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        RequireMovePhaseIdentity(phase, current);
        if (current.Phase != PartitionMovePhase.Aborting || current.PrincipalId != principal.Id
            || body.OperatorPrincipalId != principal.Id || body.TargetGrantId is null || body.TargetSettlement is null
            || JsonData.Fingerprint(current) != JsonData.Fingerprint(body.Control)
            || phase.ControlIntentDigest != PartitionMoveIntentIdentity.Digest(current))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        PartitionMoveCleanupSettlement.Require(transaction, current, PartitionMovePeerStage.Abort,
            PartitionMoveCleanupRole.Source, body.SourceGrantId, body.SourceSettlement, body.SourceBody, Limits.MaxBatchBytes);
        PartitionMoveCleanupSettlement.Require(transaction, current, PartitionMovePeerStage.Abort,
            PartitionMoveCleanupRole.Target, body.TargetGrantId.Value, body.TargetSettlement, body.TargetBody, Limits.MaxBatchBytes);
        var key = KeySpace.Partition(PartitionMoveProtocol.AbortProgressSpace, phase.Partition, phase.MoveId);
        var next = PartitionMoveProtocol.EmptyCount;
        transaction.ReadValue(key, bytes => next = NativeSerialization.Deserialize<int>(bytes));
        if (phase.PageOrdinal != next || next < PartitionMoveProtocol.EmptyCount || next >= Limits.MaxScanRecords)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        var complete = DisposeAbortedMoveGrants(transaction, current, body.SourceSettlement, body.TargetSettlement);
        var journal = MoveJournalReceipt(transaction, commandId, position, phase.ControlIntentDigest);
        transaction.Put(key, NativeSerialization.Serialize(checked(next + PartitionMoveProtocol.SequenceStep)));
        var progress = new PartitionMoveCleanupState(PartitionMoveProtocol.Version, phase.MoveId,
            phase.Partition, phase.ControlIntentDigest, phase.Stage, PartitionMoveCleanupRole.Source,
            PartitionMoveProtocol.EmptyCount, complete ? journal : null, next + PartitionMoveProtocol.SequenceStep);
        return new(phase.MoveId, phase.Stage, journal, current, null, null, null, null, progress);
    }
}
