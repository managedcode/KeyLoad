using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ReplicatedOperation CreateVerifiedPartitionMovementRetireCancellation(string principalId,
        PartitionMoveRetireCancellationBody incoming, ReadExecutionBudget work)
    {
        movementCheckpointVerifier.RequireReceiverAdministrator(this, principalId, work);
        if (incoming.ActualReceiverPrincipalId is not null
            || incoming.ActualReceiverPolicyEpoch != PartitionMoveProtocol.EmptyCount)
        { throw Errors.Fail(ErrorCode.PermissionDenied, PartitionMoveProtocol.MissingAuthority); }
        var ownedBytes = NativeSerialization.Serialize(incoming);
        RequireNativeBudget(ownedBytes.Length);
        var owned = NativeSerialization.Deserialize<PartitionMoveRetireCancellationBody>(ownedBytes);
        var principal = Store.Read(view => RequireMoveDispatchPrincipal(work.CreateView(view), principalId,
            EvaluationClock.GetUtcNow()));
        owned = owned with { ActualReceiverPrincipalId = principal.Id, ActualReceiverPolicyEpoch = principal.PolicyEpoch };
        Store.Read(view =>
        {
            RequireMoveRetireCancellationScope(work.CreateView(view), owned, principal.Id,
                principal.PolicyEpoch, EvaluationClock.GetUtcNow());
            return true;
        });
        var payload = NativeSerialization.Serialize(owned);
        RequireNativeBudget(payload.Length);
        var verified = movementCheckpointVerifier.VerifyRetireCancellation(this, principal.Id, payload, work);
        if (verified != Convert.ToHexStringLower(SHA256.HashData(payload)))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMoveProtocol.MissingAuthority); }
        var original = owned.OriginalEnvelope;
        var phase = new PartitionMovePhaseCommand(original.Version, original.MoveId, original.Partition,
            original.ControlOwner, original.SourcePlacement, original.DestinationOwner, original.ControlIntentDigest,
            PartitionMovePeerStage.RetireCancel, original.PageOrdinal, payload,
            Resources: ImmutableArray<ResourceDefinition>.Empty);
        work.Check();
        return IssueNativeOperation(new(owned.CancellationCommandId, OperationKind.PartitionMovementPhase,
            principal.Id, EvaluationClock.GetUtcNow(), MovementPhaseIdentityJson(phase)),
            new NativeCommandPayload(NativeSerialization.Serialize(phase)));
    }
    private PartitionMovePhaseResult ExecuteExpiredRetireCancellation(IAtomicTransaction transaction,
        PrincipalRecord principal, ReplicatedOperation operation, PartitionMovePhaseCommand phase, long position)
    {
        var body = NativeSerialization.Deserialize<PartitionMoveRetireCancellationBody>(phase.Body.Span);
        var original = body.OriginalEnvelope;
        if (!principal.ClusterAdministrator || operation.Id != body.CancellationCommandId
            || phase.Stage != PartitionMovePeerStage.RetireCancel || phase.MoveId != original.MoveId
            || phase.Partition != original.Partition || phase.PageOrdinal != original.PageOrdinal
            || phase.ControlIntentDigest != original.ControlIntentDigest || phase.GrantId is not null
            || phase.Resources.IsDefault || !phase.Resources.IsEmpty)
        { throw Errors.Fail(ErrorCode.PermissionDenied, PartitionMoveProtocol.MissingAuthority); }
        RequireMoveRetireCancellationScope(transaction, body, principal.Id, principal.PolicyEpoch, operation.EvaluatedAt);
        var previous = PartitionMoveRetireCancellationStorage.Read(transaction, original.Partition,
            original.MoveId, body.OriginalPhaseCommandId, Limits.MaxBatchBytes);
        if (previous is not null)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        var outcome = CommandOutcomeKeyResolver.Select(transaction, principal.Id, body.OriginalPhaseCommandId,
            new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, original.Partition)).Outcome;
        if (outcome is not null)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        var originalPhase = new PartitionMovePhaseCommand(original.Version, original.MoveId, original.Partition,
            original.ControlOwner, original.SourcePlacement, original.DestinationOwner, original.ControlIntentDigest,
            original.Stage, original.PageOrdinal, original.Body, original.Grant!.GrantId, original.Grant.Resources);
        var cleanup = NativeSerialization.Deserialize<PartitionMoveCleanupBody>(original.Body.Span);
        RequireMovePhaseIdentity(originalPhase, cleanup.Control);
        RequireMoveCleanupRole(transaction, cleanup, originalPhase);
        var retained = PartitionMoveCleanupStorage.Read(transaction, original.Partition, original.MoveId, Limits.MaxBatchBytes);
        if (retained is null
            ? cleanup.FamilyOrdinal != PartitionMoveProtocol.EmptyCount || original.PageOrdinal != PartitionMoveProtocol.EmptyCount
            : retained.Stage != PartitionMovePeerStage.Retire || retained.Role != PartitionMoveCleanupRole.Source
                || retained.ControlIntentDigest != original.ControlIntentDigest || retained.Completion is not null
                || retained.NextFamily != cleanup.FamilyOrdinal || retained.NextBatch != original.PageOrdinal)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        var receipt = MoveJournalReceipt(transaction, operation.Id, position, phase.ControlIntentDigest);
        var cancellation = new PartitionMoveExpiredRetireCancellation(PartitionMoveProtocol.Version,
            body.OriginalPhaseCommandId, PartitionMoveOriginalDispatchIdentity.Digest(body.OriginalPhaseCommandId, original),
            original.Nonce, original.ExpiresAt, operation.Id, receipt, principal.Id, principal.PolicyEpoch,
            body.CleanupGeneration, cleanup.FamilyOrdinal, original.PageOrdinal, phase);
        PartitionMoveRetireCancellationStorage.Write(transaction, cancellation, Limits.MaxBatchBytes,
            movementCheckpoints.MaxPhaseRecordsPerMove, movementCheckpoints.MaxRetainedMetadataBytesPerMove);
        return new(phase.MoveId, PartitionMovePeerStage.RetireCancel, receipt, null, null, null, null,
            RetireCancellation: cancellation);
    }

}
