using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Owns the original effect native proof checks with the same configured authority and operation work.</summary>
internal sealed class PartitionMovementReceiverEffectVerifier(IOptions<NodeOptions> options, IOptions<ReplicaConfiguration> replicaConfiguration, PartitionMovementReceiverIssuerVerifier issuer, PartitionMovementReceiverCheckpointProofs checkpoint)
{
    internal static void RequireOriginalEffectTransportNonce(PartitionMovePeerEnvelope envelope,
        PartitionMoveParentPhase pending, PartitionMoveParentState state)
    {
        if (envelope.Nonce == pending.OriginalRequestNonce)
        { return; }
        var captureAck = pending.CaptureProofCheckpointReceipt;
        if (envelope.Stage != PartitionMovePeerStage.Capture || pending.Stage != PartitionMovePeerStage.Capture
            || pending.OriginalCaptureReleaseNonce == Guid.Empty
            || pending.OriginalCaptureReleaseNonce == pending.OriginalRequestNonce
            || envelope.Nonce != pending.OriginalCaptureReleaseNonce
            || pending.OriginalDescriptor is null || pending.OriginalFence is null
            || pending.OriginalCaptureWitness is null || captureAck is null
            || captureAck.CommandId == Guid.Empty || captureAck.AppliedPosition <= PartitionMovementProtocol.NoAppliedPosition
            || state.CurrentReadCut < captureAck.AppliedPosition
            || !PhysicalOwnerEntryValidation.SameOwner(captureAck.PhysicalOwner, envelope.ControlOwner))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
    }

    internal void RequireRetainedIssuerPacket(DatabaseEngine database, PartitionMoveParentPhase pending,
        ReadExecutionBudget work)
    {
        var packet = pending.OriginalReceiverIssuePacket
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var source = pending.OriginalReceiverSourceWitness
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var authorization = pending.OriginalAuthorization
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var phase = pending.OriginalPhase
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var original = new PartitionMovePeerEnvelope(phase.Version, phase.MoveId, phase.Partition,
            phase.ControlOwner, phase.SourcePlacement, phase.DestinationOwner, phase.ControlIntentDigest,
            phase.Stage, phase.PageOrdinal, pending.OriginalExpiresAt, pending.OriginalRequestNonce,
            phase.Body, pending.OriginalGrant);
        var body = new PartitionMoveReceiverIssueBody(PartitionMoveProtocol.Version,
            pending.OriginalPhaseCommandId, original, authorization, source.OriginalReplyBytes,
            SourcePendingSignature: source.OriginalReplySignature,
            OriginalIssuerRequestBytes: packet.OriginalRequestBytes,
            OriginalIssuerRequestSignature: packet.OriginalRequestSignature);
        issuer.RequireNativeIssuerPacket(database, body, work);
        var actual = NativeSerialization.Deserialize<PartitionMovementReceiverIssueRequest>(packet.OriginalRequestBytes.Span);
        if (packet.Version != PartitionMoveProtocol.Version
            || packet.OriginalPhaseCommandId != pending.OriginalPhaseCommandId
            || packet.IssuanceNonce != actual.IssuanceNonce
            || !NativeSerialization.Serialize(actual.SourceProofCheckpointReceipt).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(pending.ReceiverSourceCheckpointReceipt)))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        work.Check();
    }

    internal string VerifyReceiverEffect(DatabaseEngine database, string principalId, Guid originalCommandId,
        ReadOnlyMemory<byte> ownedEnvelope, ReadExecutionBudget work)
    {
        PartitionMovementReceiverIssuerVerifier.RequireReceiverAdministrator(database, principalId, work);
        if (ownedEnvelope.IsEmpty || ownedEnvelope.Length > database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMovementProtocol.InvalidProof); }
        var original = NativeSerialization.Deserialize<PartitionMovePeerEnvelope>(ownedEnvelope.Span);
        var source = original.SourceDispatchWitness
            ?? throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof);
        var receiver = original.ReceiverIssuanceProof
            ?? throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof);
        if (original.Grant is not { RequireReceiverIssuance: true } || source.OriginalPhaseCommandId != originalCommandId
            || source.OriginalReplyBytes.IsEmpty || source.OriginalReplyBytes.Length > database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        var reply = NativeSerialization.Deserialize<PartitionMovementSourcePendingReply>(source.OriginalReplyBytes.Span);
        var read = GrainNativePayload.Read<GrainValue>(reply.Reply.Payload).Value as PartitionMovementParentStateResult
            ?? throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof);
        var pending = read.State.Pending
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var authorization = pending.OriginalAuthorization
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        RequireOriginalEffectTransportNonce(original, pending, read.State);
        var unsealed = original with
        {
            Nonce = pending.OriginalRequestNonce,
            ReceiverIssuanceProof = null,
            SourceDispatchWitness = null
        };
        var issued = new PartitionMoveReceiverIssueBody(PartitionMoveProtocol.Version, originalCommandId, unsealed,
            authorization, source.OriginalReplyBytes, SourcePendingSignature: source.OriginalReplySignature);
        PartitionMovementReceiverSourceProofVerifier.Require(database, issued, work, options.Value, replicaConfiguration.Value);
        RequireSourceDispatchProof(originalCommandId, source, receiver, reply, read.State);
        RequireRetainedIssuerPacket(database, pending, work);
        checkpoint.RequireReceiverWitnessMac(database, pending, receiver);
        RequireReceiverEffectSnapshot(database, originalCommandId, receiver, pending, work);
        work.Check();
        return Convert.ToHexStringLower(SHA256.HashData(ownedEnvelope.Span));
    }

    internal static void RequireSourceDispatchProof(Guid originalCommandId, PartitionMoveReceiverSourceWitness source,
        PartitionMoveReceiverIssuanceWitness receiver, PartitionMovementSourcePendingReply reply,
        PartitionMoveParentState state)
    {
        var pending = state.Pending
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        if (source.Version != PartitionMoveProtocol.Version || reply.Nonce != source.QueryNonce
            || source.QueryNonce == Guid.Empty || source.QueryNonce == pending.OriginalRequestNonce
            || pending.OriginalPhaseCommandId != originalCommandId || pending.ReceiverSourceCheckpointReceipt is null
            || pending.ReceiverIssuanceCheckpointReceipt is null || pending.OriginalReceiverIssuanceWitness is null
            || pending.OriginalReceiverIssuePacket is null || pending.ReceiverIssuePacketCheckpointReceipt is null
            || pending.ReceiverSourceCheckpointReceipt.CommandId == Guid.Empty
            || pending.ReceiverIssuanceCheckpointReceipt.CommandId == Guid.Empty
            || pending.ReceiverIssuePacketCheckpointReceipt.CommandId == Guid.Empty
            || state.CurrentReadCut < pending.ReceiverIssuanceCheckpointReceipt.AppliedPosition
            || !PhysicalOwnerEntryValidation.SameOwner(pending.ReceiverSourceCheckpointReceipt.PhysicalOwner, reply.ControlOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(pending.ReceiverIssuanceCheckpointReceipt.PhysicalOwner, reply.ControlOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(pending.ReceiverIssuePacketCheckpointReceipt.PhysicalOwner, reply.ControlOwner)
            || pending.ReceiverSourceCheckpointReceipt.AppliedPosition >= pending.ReceiverIssuePacketCheckpointReceipt.AppliedPosition
            || pending.ReceiverIssuePacketCheckpointReceipt.AppliedPosition >= pending.ReceiverIssuanceCheckpointReceipt.AppliedPosition
            || !NativeSerialization.Serialize(pending.OriginalReceiverIssuanceWitness).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(receiver)))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
    }

    internal void RequireReceiverEffectSnapshot(DatabaseEngine database, Guid originalCommandId,
        PartitionMoveReceiverIssuanceWitness witness, PartitionMoveParentPhase pending, ReadExecutionBudget work)
    {
        var reply = NativeSerialization.Deserialize<PartitionMovementTransportReply>(witness.OriginalReplyBytes.Span);
        if (witness.Version != PartitionMoveProtocol.Version || witness.OriginalPhaseCommandId != originalCommandId
            || reply.CommandId != originalCommandId || reply.Nonce != witness.QueryNonce || witness.QueryNonce == Guid.Empty
            || witness.QueryNonce == pending.OriginalRequestNonce || reply.Reply.Error is not null
            || reply.OriginalPhaseIdentityDigest != pending.OriginalPhaseIdentityDigest
            || !PhysicalOwnerEntryValidation.SameOwner(reply.Receiver, pending.OriginalReceiverOwner)
            || !pending.OriginalReceiverOwner.VoterIds.Contains(reply.Discovery.VoterId)
            || reply.Discovery.Incarnation != pending.OriginalReceiverOwner.Incarnation
            || reply.Discovery.ClusterId != options.Value.ClusterId)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        var actual = GrainNativePayload.Read<GrainValue>(reply.Reply.Payload).Value as PartitionMovementReceiverIssuanceResult
            ?? throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof);
        PartitionMovementReceiverCheckpointProofs.RequireReceiverSnapshot(database, pending, actual.Snapshot);
        work.CheckResult(actual);
    }
}
