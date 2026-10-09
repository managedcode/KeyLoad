using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementCaptureProofVerifier
{
    internal static void Require(DatabaseEngine database, PartitionMoveCheckpointBody body,
        PartitionMoveParentPhase original, PartitionMoveCaptureWitness witness, ReadExecutionBudget work, NodeOptions options)
    {
        var grant = original.OriginalGrant
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        if (original.Stage != PartitionMovePeerStage.Capture || witness.Version != PartitionMoveProtocol.Version
            || witness.OriginalPhaseCommandId != original.OriginalPhaseCommandId
            || witness.OriginalRequestNonce == Guid.Empty || witness.OriginalRequestNonce != original.OriginalRequestNonce
            || body.OriginalRequestNonce != original.OriginalRequestNonce
            || body.OriginalExpiresAt != original.OriginalExpiresAt || grant.ExpiresAt != original.OriginalExpiresAt
            || body.OriginalCaptureReleaseNonce != original.OriginalCaptureReleaseNonce
            || grant.PhaseCommandId != original.OriginalPhaseCommandId || grant.Stage != original.Stage
            || grant.BodyDigest != original.OriginalBodyDigest
            || !PhysicalOwnerEntryValidation.SameOwner(grant.ReceiverOwner, original.OriginalReceiverOwner)
            || witness.OriginalReplyBytes.IsEmpty || witness.OriginalReplyBytes.Length > database.Limits.MaxBatchBytes
            || string.IsNullOrWhiteSpace(witness.OriginalReplySignature))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        var key = Convert.FromBase64String(original.OriginalReceiverOwner.PhysicalShardId == options.PhysicalShardId
            && original.OriginalReceiverOwner.Incarnation == database.Store.Identity.Incarnation
            ? options.PeerSecret : options.MembershipAuthority.TrustedGroupPeerSecret
                ?? throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof));
        try
        {
            using var mac = new PartitionMovementMac(key, database.Limits.MaxBatchBytes);
            if (!mac.Verify(witness.OriginalReplyBytes.Span, witness.OriginalReplySignature, reply: true))
            { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        work.Check();
        var reply = NativeSerialization.Deserialize<PartitionMovementTransportReply>(witness.OriginalReplyBytes.Span);
        if (reply.CommandId != original.OriginalPhaseCommandId || reply.Nonce != original.OriginalRequestNonce
            || reply.OriginalPhaseIdentityDigest != original.OriginalPhaseIdentityDigest
            || !PhysicalOwnerEntryValidation.SameOwner(reply.Receiver, original.OriginalReceiverOwner)
            || !original.OriginalReceiverOwner.VoterIds.Contains(reply.Discovery.VoterId)
            || reply.Discovery.Incarnation != original.OriginalReceiverOwner.Incarnation
            || reply.Discovery.ClusterId != options.ClusterId || reply.Reply.Error is not null)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        var handle = GrainNativePayload.Read<GrainValue>(reply.Reply.Payload).Value as PartitionMovementCaptureHandle
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof);
        RequireDescriptor(database, body, original, handle);
        work.CheckResult(handle);
    }

    private static void RequireDescriptor(DatabaseEngine database, PartitionMoveCheckpointBody body,
        PartitionMoveParentPhase original, PartitionMovementCaptureHandle handle)
    {
        var descriptor = body.OriginalDescriptor
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var fence = original.OriginalPhase is { } phase
            ? NativeSerialization.Deserialize<PartitionMoveCaptureRequest>(phase.Body.Span).Fence
            : original.OriginalFence ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        PartitionMoveDescriptorValidation.Require(descriptor, fence, database.Limits);
        if (handle.HandleId == Guid.Empty || handle.ExpiresAt != original.OriginalExpiresAt
            || handle.PageCount != descriptor.Families.Sum(family => family.PageCount)
            || body.OriginalFence is null
            || !NativeSerialization.Serialize(fence).AsSpan().SequenceEqual(NativeSerialization.Serialize(body.OriginalFence))
            || !NativeSerialization.Serialize(handle.Descriptor).AsSpan().SequenceEqual(NativeSerialization.Serialize(descriptor))
            || original.OriginalDescriptor is { } retained
                && !NativeSerialization.Serialize(retained).AsSpan().SequenceEqual(NativeSerialization.Serialize(descriptor))
            || original.OriginalCaptureWitness is { } proof
                && !NativeSerialization.Serialize(proof).AsSpan().SequenceEqual(NativeSerialization.Serialize(body.OriginalCaptureWitness)))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
    }
}
