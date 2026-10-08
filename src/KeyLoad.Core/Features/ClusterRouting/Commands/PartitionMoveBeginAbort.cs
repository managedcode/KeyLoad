using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionMovePhaseResult ExecuteMoveBeginAbort(IAtomicTransaction transaction,
        PrincipalRecord principal, Guid commandId, PartitionMovePhaseCommand phase, long position)
    {
        var body = NativeSerialization.Deserialize<PartitionMoveControlBody>(phase.Body.Span);
        var current = PartitionMoveControlStorage.ReadHistory(transaction, phase.Partition,
            phase.MoveId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        RequireMovePhaseIdentity(phase, current);
        if (body.OperatorPrincipalId != principal.Id || current.PrincipalId != principal.Id
            || JsonData.Fingerprint(body.Control) != JsonData.Fingerprint(current)
            || phase.ControlIntentDigest != PartitionMoveIntentIdentity.Digest(current)
            || current.Phase is PartitionMovePhase.Published or PartitionMovePhase.Retired or PartitionMovePhase.Aborted)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var aborting = current with { Phase = PartitionMovePhase.Aborting };
        PartitionMoveControlStorage.Write(transaction, aborting, Limits.MaxBatchBytes);
        return new(phase.MoveId, phase.Stage, MoveJournalReceipt(transaction, commandId, position,
            phase.ControlIntentDigest), aborting, null, null, null);
    }
}
