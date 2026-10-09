using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementOutcomeProofVerifier
{
    internal static void Require(DatabaseEngine database, PartitionMoveCheckpointBody body,
        PartitionMoveParentPhase original, PartitionMoveAuthenticatedOutcomeWitness witness, ReadExecutionBudget work, NodeOptions options)
    {
        if (PartitionMoveGrantValidation.IsLocalControl(original.Stage)
            || witness.Version != PartitionMoveProtocol.Version || !Enum.IsDefined(witness.Purpose)
            || witness.OriginalPhaseCommandId != original.OriginalPhaseCommandId || witness.ReplyNonce == Guid.Empty
            || body.OriginalExpiresAt != original.OriginalExpiresAt || body.OriginalRequestNonce != original.OriginalRequestNonce
            || body.OriginalCaptureReleaseNonce != original.OriginalCaptureReleaseNonce
            || witness.OriginalReplyBytes.IsEmpty || witness.OriginalReplyBytes.Length > database.Limits.MaxBatchBytes
            || string.IsNullOrWhiteSpace(witness.OriginalReplySignature))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        var expectedNonce = original.Stage == PartitionMovePeerStage.Capture
            ? original.OriginalCaptureReleaseNonce : original.OriginalRequestNonce;
        if (witness.Purpose == PartitionMoveOutcomeProofPurpose.EffectReply && witness.ReplyNonce != expectedNonce)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        if (witness.Purpose == PartitionMoveOutcomeProofPurpose.OutcomeReadReply
            && (witness.ReplyNonce == original.OriginalRequestNonce
                || witness.ReplyNonce == original.OriginalCaptureReleaseNonce))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        VerifyOutcomeMac(database, original, witness, options);
        work.Check();
        var reply = NativeSerialization.Deserialize<PartitionMovementTransportReply>(witness.OriginalReplyBytes.Span);
        if (reply.CommandId != original.OriginalPhaseCommandId || reply.Nonce != witness.ReplyNonce
            || reply.OriginalPhaseIdentityDigest != original.OriginalPhaseIdentityDigest
            || !PhysicalOwnerEntryValidation.SameOwner(reply.Receiver, original.OriginalReceiverOwner)
            || !original.OriginalReceiverOwner.VoterIds.Contains(reply.Discovery.VoterId)
            || reply.Discovery.Incarnation != original.OriginalReceiverOwner.Incarnation
            || reply.Discovery.ClusterId != options.ClusterId || reply.Reply.Error is not null)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        var value = GrainNativePayload.Read<GrainValue>(reply.Reply.Payload).Value;
        var actual = witness.Purpose == PartitionMoveOutcomeProofPurpose.OutcomeReadReply
            ? (value as PartitionMovementOutcomeWitness)?.Result
            : value is PartitionMovePhaseResult phase ? new OperationResult(null) { NativeValue = phase } : null;
        if (actual is null || body.ObservedOriginalResult is null
            || !NativeSerialization.Serialize(actual).AsSpan().SequenceEqual(NativeSerialization.Serialize(body.ObservedOriginalResult)))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        work.CheckResult(actual);
    }

    private static void VerifyOutcomeMac(DatabaseEngine database, PartitionMoveParentPhase original,
        PartitionMoveAuthenticatedOutcomeWitness witness, NodeOptions options)
    {
        if (original.OriginalGrant is null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof); }
        var key = Convert.FromBase64String(original.OriginalReceiverOwner.PhysicalShardId == options.PhysicalShardId
            && original.OriginalReceiverOwner.Incarnation == database.Store.Identity.Incarnation
            ? options.PeerSecret : options.MembershipAuthority.TrustedGroupPeerSecret
                ?? throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof));
        try
        {
            using var mac = new PartitionMovementMac(key, database.Limits.MaxBatchBytes);
            var verified = witness.Purpose == PartitionMoveOutcomeProofPurpose.OutcomeReadReply
                ? mac.VerifyOutcome(witness.OriginalReplyBytes.Span, witness.OriginalReplySignature, reply: true)
                : mac.Verify(witness.OriginalReplyBytes.Span, witness.OriginalReplySignature, reply: true);
            if (!verified)
            { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
