using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Queries;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static bool MatchesMoveReceiver(PartitionMovePhaseCommand phase, PhysicalShardRecord receiver)
    {
        var sourceMatches = receiver.PhysicalShardId == phase.SourcePlacement.PhysicalShardId
            && receiver.Incarnation == phase.SourcePlacement.Incarnation
            && receiver.VoterIds.SequenceEqual(phase.SourcePlacement.VoterIds, StringComparer.Ordinal);
        return phase.Stage switch
        {
            PartitionMovePeerStage.Fence or PartitionMovePeerStage.Capture or PartitionMovePeerStage.Retire
                or PartitionMovePeerStage.SourceBeginAbort
                => sourceMatches,
            PartitionMovePeerStage.StagePage or PartitionMovePeerStage.Install or PartitionMovePeerStage.PublishWitness
                or PartitionMovePeerStage.ControlApplyCommand
                => PhysicalOwnerEntryValidation.SameOwner(phase.DestinationOwner, receiver),
            PartitionMovePeerStage.Abort => sourceMatches
                || PhysicalOwnerEntryValidation.SameOwner(phase.DestinationOwner, receiver),
            _ => false
        };
    }

    private PartitionMovePhaseResult ExecuteMoveAuthorize(IAtomicTransaction transaction,
        PrincipalRecord principal, Guid commandId, PartitionMovePhaseCommand phase,
        DateTimeOffset evaluatedAt, long position)
    {
        var body = NativeSerialization.Deserialize<PartitionMoveAuthorizeBody>(phase.Body.Span);
        var control = PartitionMoveControlStorage.ReadHistory(transaction, phase.Partition,
            phase.MoveId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        RequireMovePhaseIdentity(phase, control);
        var directory = RequireMoveDirectory(transaction);
        if (body.GrantId != commandId || body.GrantId == Guid.Empty || body.PhaseCommandId == Guid.Empty
            || body.PhaseCommandId == body.GrantId || body.OperatorPrincipalId != principal.Id
            || control.PrincipalId != principal.Id || control.PolicyEpoch > principal.PolicyEpoch
            || body.ExpiresAt <= evaluatedAt || body.Phase.GrantId is not null
            || PartitionMoveGrantValidation.IsLocalControl(body.Phase.Stage)
            || body.Phase.MoveId != control.MoveId || body.Phase.Partition != control.Partition
            || body.Phase.ControlIntentDigest != PartitionMoveIntentIdentity.Digest(control)
            || !PhysicalOwnerEntryValidation.SameOwner(directory.ControlOwner, phase.ControlOwner)
            || !directory.Owners.Any(owner => PhysicalOwnerEntryValidation.SameOwner(owner.Owner, body.ReceiverOwner))
            || !MatchesMoveReceiver(body.Phase, body.ReceiverOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        RequireMovePhaseIdentity(body.Phase, control);
        PartitionMoveGrantStageAdmission.Require(transaction, body.Phase, control, Limits.MaxBatchBytes);
        var digest = Convert.ToHexStringLower(SHA256.HashData(body.Phase.Body.Span));
        var previous = PartitionMoveGrantStorage.Read(transaction, phase.Partition, body.GrantId, Limits.MaxBatchBytes);
        var grant = new PartitionMovePhaseGrant(PartitionMoveProtocol.Version, body.GrantId, body.PhaseCommandId,
            phase.MoveId, phase.Partition, directory.ControlOwner, body.ReceiverOwner, principal.Id,
            principal.PolicyEpoch, body.Phase.Stage, body.Phase.ControlIntentDigest, digest, body.ExpiresAt,
            position, null, PartitionMoveResources.Capture(transaction, phase.Partition, Limits))
            with
        { PageOrdinal = body.Phase.PageOrdinal };
        if (body.Phase.Stage is PartitionMovePeerStage.Abort or PartitionMovePeerStage.Retire
            or PartitionMovePeerStage.SourceBeginAbort)
        {
            var cleanup = NativeSerialization.Deserialize<PartitionMoveCleanupBody>(body.Phase.Body.Span);
            grant = grant with
            {
                CleanupRole = cleanup.Role,
                CleanupFamily = cleanup.FamilyOrdinal,
                PrecedingGrantId = cleanup.PrecedingGrantId
            };
        }
        if (previous is not null)
        {
            if (previous.PhaseCommandId != grant.PhaseCommandId || previous.BodyDigest != digest
                || previous.Stage != grant.Stage || previous.PageOrdinal != grant.PageOrdinal || previous.ExpiresAt != grant.ExpiresAt
                || previous.OperatorPolicyEpoch != grant.OperatorPolicyEpoch
                || !PhysicalOwnerEntryValidation.SameOwner(previous.ReceiverOwner, grant.ReceiverOwner))
            { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
            grant = previous;
        }
        else
        {
            PartitionMoveGrantStorage.ChangeOutstanding(transaction,
                PartitionMoveGrantStorage.MoveCountKey(phase.Partition, phase.MoveId), true, Limits.MaxBatchMutations);
            PartitionMoveGrantStorage.ChangeOutstanding(transaction, principal.Id, true, Limits.MaxBatchMutations);
            PartitionMoveGrantStorage.ChangeOutstanding(transaction,
                PartitionMoveGrantStorage.DatabaseKey(phase.Partition.TenantId, phase.Partition.DatabaseId),
                true, Limits.MaxBatchMutations);
            PartitionMoveGrantStorage.Write(transaction, grant, Limits.MaxBatchBytes);
        }
        return new(phase.MoveId, phase.Stage, MoveJournalReceipt(transaction, commandId,
            grant.AdmissionPosition, grant.ControlIntentDigest), control, null, null, null, grant);
    }
}
