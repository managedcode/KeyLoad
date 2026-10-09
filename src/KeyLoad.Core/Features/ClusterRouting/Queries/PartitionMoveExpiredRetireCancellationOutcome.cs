using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal PartitionMoveRetireCancellationSnapshot ReadPartitionMovementRetireCancellation(string principalId,
        PartitionMoveRetireCancellationReadBody body, ReadExecutionBudget work, ReadExecutionBudgetReadGrant grant)
    {
        movementCheckpointVerifier.RequireReceiverAdministrator(this, principalId, work);
        ValidatePartition(body.OriginalEnvelope.Partition);
        var bytes = NativeSerialization.Serialize(body);
        RequireNativeBudget(bytes.Length);
        if (movementCheckpointVerifier.VerifyRetireCancellationQuery(this, principalId, bytes, work)
            != Convert.ToHexStringLower(SHA256.HashData(bytes)))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMoveProtocol.MissingAuthority); }
        var result = Store.Read(view =>
        {
            var charged = work.CreateView(view, grant);
            var principal = RequireMoveDispatchPrincipal(charged, principalId, EvaluationClock.GetUtcNow());
            var original = body.OriginalEnvelope;
            PartitionMovePeerEnvelopeValidation.RequireStructure(original, Limits.MaxBatchBytes);
            if (body.Version != PartitionMoveProtocol.Version || body.QueryExpiresAt <= EvaluationClock.GetUtcNow()
                || body.OriginalPhaseCommandId == Guid.Empty || body.CancellationCommandId == Guid.Empty
                || body.OriginalPhaseCommandId == body.CancellationCommandId || original.Stage != PartitionMovePeerStage.Retire
                || original.Grant is null || original.Grant.PhaseCommandId != body.OriginalPhaseCommandId)
            { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority); }
            var cancellation = PartitionMoveRetireCancellationStorage.Read(charged, original.Partition, original.MoveId,
                body.OriginalPhaseCommandId, Limits.MaxBatchBytes);
            var actual = CommandOutcomeKeyResolver.Select(charged, principal.Id, body.CancellationCommandId,
                new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, original.Partition)).Outcome;
            var applied = charged.ReadOwnedValue(KeySpace.AppliedBytes)
                ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
            var cut = NativeSerialization.Deserialize<long>(applied);
            if (cancellation is not null)
            { RequireMoveRetireCancellationOutcome(cancellation, actual, body, principal.Id, cut); }
            else if (actual is not null && actual.Result.Error is null)
            { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority); }
            return new PartitionMoveRetireCancellationSnapshot(cancellation, actual?.Result, cut);
        });
        work.MeasureResult(result);
        work.Check();
        return result;
    }

    private void RequireMoveRetireCancellationOutcome(PartitionMoveExpiredRetireCancellation cancellation,
        StoredOutcome? actual, PartitionMoveRetireCancellationReadBody query, string principalId, long cut)
    {
        var original = query.OriginalEnvelope;
        var scope = NativeSerialization.Deserialize<PartitionMoveRetireCancellationBody>(cancellation.CancellationPhase.Body.Span);
        if (actual is null || actual.Result.Error is not null || actual.Incarnation != Store.Identity.Incarnation
            || actual.PolicyEpoch != cancellation.CancellationPolicyEpoch
            || cancellation.CancellationPrincipalId != principalId || cancellation.CancellationCommandId != query.CancellationCommandId
            || cancellation.OriginalPhaseCommandId != query.OriginalPhaseCommandId
            || cancellation.OriginalPhaseIdentityDigest != PartitionMoveOriginalDispatchIdentity.Digest(query.OriginalPhaseCommandId, original)
            || cancellation.OriginalRequestNonce != original.Nonce || cancellation.OriginalExpiresAt != original.ExpiresAt
            || cut < cancellation.CancellationReceipt.AppliedPosition
            || actual.Fingerprint != CommandFingerprint(new(cancellation.CancellationCommandId,
                OperationKind.PartitionMovementPhase, cancellation.CancellationPrincipalId,
                default, MovementPhaseIdentityJson(cancellation.CancellationPhase)))
            || !NativeSerialization.Serialize(scope.OriginalEnvelope).AsSpan().SequenceEqual(NativeSerialization.Serialize(original))
            || !NativeSerialization.Serialize(scope.OriginalAuthorization).AsSpan().SequenceEqual(NativeSerialization.Serialize(query.OriginalAuthorization)))
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority); }
        var result = actual.Result.Get<PartitionMovePhaseResult>();
        if (result.Stage != PartitionMovePeerStage.RetireCancel || result.MoveId != original.MoveId
            || !NativeSerialization.Serialize(result.Journal).AsSpan().SequenceEqual(NativeSerialization.Serialize(cancellation.CancellationReceipt))
            || !NativeSerialization.Serialize(result.RetireCancellation).AsSpan().SequenceEqual(NativeSerialization.Serialize(cancellation)))
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority); }
    }
}
