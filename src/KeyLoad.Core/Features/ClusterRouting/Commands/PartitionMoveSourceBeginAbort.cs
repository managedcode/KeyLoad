using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionMovePhaseResult ExecuteMoveSourceBeginAbort(IAtomicTransaction transaction,
        Guid commandId, PartitionMovePhaseCommand phase, long position)
    {
        var body = NativeSerialization.Deserialize<PartitionMoveCleanupBody>(phase.Body.Span);
        RequireMovePhaseIdentity(phase, body.Control);
        if (body.Control.Phase != PartitionMovePhase.Aborting || body.Role != PartitionMoveCleanupRole.Source
            || body.FamilyOrdinal != PartitionMoveCleanupFamilies.All.Length
            || body.PrecedingGrantId == Guid.Empty || body.OperatorPrincipalId != body.Control.PrincipalId)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        RequireMoveCleanupOwner(transaction, body, phase);
        var state = PartitionMoveCleanupStorage.Read(transaction, phase.Partition, phase.MoveId, Limits.MaxBatchBytes);
        if (state is not null)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        state = new(PartitionMoveProtocol.Version, phase.MoveId, phase.Partition, phase.ControlIntentDigest,
            PartitionMovePeerStage.Abort, PartitionMoveCleanupRole.Source,
            PartitionMoveCleanupFamilies.All.Length, null, PartitionMoveProtocol.EmptyCount);
        PartitionMoveTargetStorage.Write(transaction, PartitionMoveCleanupStorage.Key(phase.Partition,
            phase.MoveId), state, Limits.MaxBatchBytes);
        return new(phase.MoveId, phase.Stage, MoveJournalReceipt(transaction, commandId, position,
            phase.ControlIntentDigest), null, null, null, null, null, state);
    }
}
