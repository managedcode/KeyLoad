using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;
using KeyLoad.Replication;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Verifies actual current source pending authority before a receiver may capture its own first epoch.</summary>
internal static class PartitionMovementReceiverSourceProofVerifier
{
    internal static void Require(DatabaseEngine database, PartitionMoveReceiverIssueBody body,
        ReadExecutionBudget work, NodeOptions options, ReplicaConfiguration configuration)
    {
        work.Check();
        var original = body.OriginalEnvelope;
        var grant = original.Grant
            ?? throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof);
        if (body.SourcePendingProof.IsEmpty || body.SourcePendingProof.Length > database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        var key = Convert.FromBase64String(PartitionMovementControlProofKey.Resolve(options,
            configuration, original.ControlOwner));
        try
        {
            using var mac = new PartitionMovementMac(key, database.Limits.MaxBatchBytes);
            if (!mac.VerifyReceiverIssue(body.SourcePendingProof.Span, body.SourcePendingSignature, source: true))
            { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        var reply = NativeSerialization.Deserialize<PartitionMovementSourcePendingReply>(body.SourcePendingProof.Span);
        if (reply.Version != PartitionMoveProtocol.Version || reply.RequestId == Guid.Empty
            || reply.Nonce == Guid.Empty || reply.Nonce == original.Nonce || reply.ExpiresAt <= database.EvaluationClock.GetUtcNow()
            || !PhysicalOwnerEntryValidation.SameOwner(reply.ControlOwner, original.ControlOwner)
            || reply.SourceOperatorPrincipalId != grant.OperatorPrincipalId
            || reply.CurrentSourceOperatorPolicyEpoch != grant.OperatorPolicyEpoch
            || !original.ControlOwner.VoterIds.Contains(reply.Discovery.VoterId)
            || reply.Discovery.Incarnation != original.ControlOwner.Incarnation
            || reply.Discovery.ClusterId != options.ClusterId || reply.Reply.Error is not null)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        var result = GrainNativePayload.Read<GrainValue>(reply.Reply.Payload).Value as PartitionMovementParentStateResult
            ?? throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof);
        RequireActualPending(body, reply, result.State);
        work.CheckResult(result);
    }

    private static void RequireActualPending(PartitionMoveReceiverIssueBody body,
        PartitionMovementSourcePendingReply reply, PartitionMoveParentState state)
    {
        var original = body.OriginalEnvelope;
        var pending = state.Pending;
        var header = state.Header;
        if (header is null || pending is null || pending.OriginalPhase is null || header.TerminalResult is not null
            || pending.OriginalResult is not null || pending.OriginalGrant is not { RequireReceiverIssuance: true }
            || pending.OriginalPhaseCommandId != body.OriginalPhaseCommandId
            || pending.OriginalRequestNonce != original.Nonce || pending.OriginalExpiresAt != original.ExpiresAt
            || pending.OriginalPhaseIdentityDigest != PartitionMoveOriginalDispatchIdentity.Digest(body.OriginalPhaseCommandId, original)
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
