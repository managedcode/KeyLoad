using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionMovePhaseResult ExecuteMoveCompleteCleanup(IAtomicTransaction transaction,
        PrincipalRecord principal, Guid commandId, PartitionMovePhaseCommand phase, long position)
    {
        var body = NativeSerialization.Deserialize<PartitionMoveCompletionBody>(phase.Body.Span);
        var current = PartitionMoveControlStorage.ReadHistory(transaction, phase.Partition,
            phase.MoveId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        RequireMovePhaseIdentity(phase, current);
        var abort = phase.Stage == PartitionMovePeerStage.ControlFinalizeAbort;
        if (current.PrincipalId != principal.Id || body.OperatorPrincipalId != principal.Id
            || JsonData.Fingerprint(current) != JsonData.Fingerprint(body.Control)
            || current.Phase != (abort ? PartitionMovePhase.Aborting : PartitionMovePhase.Published)
            || phase.ControlIntentDigest != PartitionMoveIntentIdentity.Digest(current))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        PartitionMoveCleanupSettlement.Require(transaction, current,
            abort ? PartitionMovePeerStage.Abort : PartitionMovePeerStage.Retire,
            PartitionMoveCleanupRole.Source, body.SourceGrantId, body.SourceSettlement,
            body.SourceBody, Limits.MaxBatchBytes);
        if (abort)
        {
            if (body.TargetGrantId is null || body.TargetSettlement is null)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
            PartitionMoveCleanupSettlement.Require(transaction, current, PartitionMovePeerStage.Abort,
                PartitionMoveCleanupRole.Target, body.TargetGrantId.Value, body.TargetSettlement,
                body.TargetBody, Limits.MaxBatchBytes);

        }
        RequireNoUnsettledMoveGrants(transaction, current);
        var completed = current with { Phase = abort ? PartitionMovePhase.Aborted : PartitionMovePhase.Retired };
        PartitionMoveControlStorage.Write(transaction, completed, Limits.MaxBatchBytes);
        PartitionMoveGrantStorage.ChangeOutstanding(transaction, principal.Id, false, Limits.MaxBatchMutations);
        PartitionMoveGrantStorage.ChangeOutstanding(transaction,
            PartitionMoveGrantStorage.DatabaseKey(phase.Partition.TenantId, phase.Partition.DatabaseId),
            false, Limits.MaxBatchMutations);
        return new(phase.MoveId, phase.Stage, MoveJournalReceipt(transaction, commandId, position,
            phase.ControlIntentDigest), completed, null, null, completed.PublishedPlacement);
    }
}
