using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;
using KeyLoad.Replication;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Fresh cancellation read authority preserves the historical original issuance epoch.</summary>
internal static class PartitionMovementRetireCancellationSourceProofVerifier
{
    internal static void Require(DatabaseEngine database, PartitionMoveRetireCancellationBody body,
        ReadExecutionBudget work, NodeOptions options, ReplicaConfiguration configuration)
    {
        work.Check();
        var original = body.OriginalEnvelope;
        var grant = original.Grant
            ?? throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof);
        var witness = body.OriginalSourceWitness
            ?? throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof);
        if (witness.Version != PartitionMoveProtocol.Version
            || witness.OriginalPhaseCommandId != body.OriginalPhaseCommandId
            || witness.QueryNonce == Guid.Empty || witness.QueryNonce == original.Nonce
            || witness.OriginalReplyBytes.IsEmpty || witness.OriginalReplyBytes.Length > database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        var key = Convert.FromBase64String(PartitionMovementControlProofKey.Resolve(options,
            configuration, original.ControlOwner));
        try
        {
            using var mac = new PartitionMovementMac(key, database.Limits.MaxBatchBytes);
            if (!mac.VerifyReceiverIssue(witness.OriginalReplyBytes.Span, witness.OriginalReplySignature, source: true))
            { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        var reply = NativeSerialization.Deserialize<PartitionMovementSourcePendingReply>(witness.OriginalReplyBytes.Span);
        if (reply.Version != PartitionMoveProtocol.Version || reply.RequestId == Guid.Empty
            || reply.Nonce != witness.QueryNonce || reply.ExpiresAt <= database.EvaluationClock.GetUtcNow()
            || !PhysicalOwnerEntryValidation.SameOwner(reply.ControlOwner, original.ControlOwner)
            || reply.SourceOperatorPrincipalId != grant.OperatorPrincipalId
            || reply.CurrentSourceOperatorPolicyEpoch <= PartitionMoveProtocol.EmptyCount
            || !original.ControlOwner.VoterIds.Contains(reply.Discovery.VoterId)
            || reply.Discovery.Incarnation != original.ControlOwner.Incarnation
            || reply.Discovery.ClusterId != options.ClusterId || reply.Reply.Error is not null)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        var result = GrainNativePayload.Read<GrainValue>(reply.Reply.Payload).Value as PartitionMovementParentStateResult
            ?? throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof);
        RequireActualPending(body, reply, result.State);
        work.CheckResult(result);
    }

    private static void RequireActualPending(PartitionMoveRetireCancellationBody body,
        PartitionMovementSourcePendingReply reply, PartitionMoveParentState state)
    {
        var original = body.OriginalEnvelope;
        var pending = state.Pending;
        var header = state.Header;
        if (header is null || pending is null || pending.OriginalPhase is null
            || header.TerminalResult is not null || pending.OriginalResult is not null
            || pending.RetireCancellation is not null || pending.Stage != PartitionMovePeerStage.Retire
            || pending.OriginalGrant is not { RequireReceiverIssuance: true } grant
            || pending.OriginalPhaseCommandId != body.OriginalPhaseCommandId
            || pending.OriginalRequestNonce != original.Nonce || pending.OriginalExpiresAt != original.ExpiresAt
            || pending.OriginalPhaseIdentityDigest != PartitionMoveOriginalDispatchIdentity.Digest(body.OriginalPhaseCommandId, original)
            || pending.OriginalIssuancePolicyEpoch != grant.OperatorPolicyEpoch
            || pending.CleanupGeneration != body.CleanupGeneration || header.CleanupGeneration != body.CleanupGeneration
            || header.OperatorPrincipalId != reply.SourceOperatorPrincipalId || header.MoveId != original.MoveId
            || header.Partition != original.Partition || state.CurrentOperatorPrincipalId != reply.SourceOperatorPrincipalId
            || state.CurrentOperatorPolicyEpoch != reply.CurrentSourceOperatorPolicyEpoch
            || state.CurrentReadCut < pending.AdmissionCheckpointReceipt.AppliedPosition
            || !NativeSerialization.Serialize(pending.OriginalGrant).AsSpan().SequenceEqual(NativeSerialization.Serialize(original.Grant))
            || !NativeSerialization.Serialize(pending.OriginalAuthorization).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(body.OriginalAuthorization)))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
    }
}
