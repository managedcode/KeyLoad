using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Owns the original checkpoint native proof checks with the same configured authority and operation work.</summary>
internal sealed class PartitionMovementReceiverCheckpointProofs(IOptions<NodeOptions> options, IOptions<ReplicaConfiguration> replicaConfiguration, PartitionMovementReceiverIssuerVerifier issuer)
{
    internal void VerifyIssuerPacketCheckpoint(DatabaseEngine database, string principalId,
        PartitionMoveCheckpointBody body, ReadExecutionBudget work)
    {
        if (body.OriginalReceiverIssuePacket is not { } packet)
        { return; }
        if (body.Action != PartitionMoveCheckpointAction.Observe || body.ObservedOriginalResult is not null)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        var leaf = work.CreateReadGrant(work.RemainingReadGrantBytes, work.RemainingReadGrantRecords);
        var state = database.ReadPartitionMovementParentState(principalId, body.OriginalTransferRequest,
            body.OriginalPhaseCommandId, work, leaf);
        work.CompleteReadGrant(leaf);
        var pending = state.Pending
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var phase = pending.OriginalPhase
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var source = pending.OriginalReceiverSourceWitness
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var authorization = pending.OriginalAuthorization
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var envelope = new PartitionMovePeerEnvelope(phase.Version, phase.MoveId, phase.Partition, phase.ControlOwner,
            phase.SourcePlacement, phase.DestinationOwner, phase.ControlIntentDigest, phase.Stage, phase.PageOrdinal,
            pending.OriginalExpiresAt, pending.OriginalRequestNonce, phase.Body, pending.OriginalGrant);
        var issued = new PartitionMoveReceiverIssueBody(PartitionMoveProtocol.Version, pending.OriginalPhaseCommandId,
            envelope, authorization, source.OriginalReplyBytes, SourcePendingSignature: source.OriginalReplySignature,
            OriginalIssuerRequestBytes: packet.OriginalRequestBytes, OriginalIssuerRequestSignature: packet.OriginalRequestSignature);
        issuer.RequireNativeIssuerPacket(database, issued, work);
        var request = NativeSerialization.Deserialize<PartitionMovementReceiverIssueRequest>(packet.OriginalRequestBytes.Span);
        if (packet.Version != PartitionMoveProtocol.Version || packet.OriginalPhaseCommandId != pending.OriginalPhaseCommandId
            || packet.IssuanceNonce != request.IssuanceNonce
            || !NativeSerialization.Serialize(request.SourceProofCheckpointReceipt).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(pending.ReceiverSourceCheckpointReceipt)))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        PartitionMovementIssuerSourceAckVerifier.Require(database, principalId, state, pending, work);
    }

    internal void VerifyReceiverCheckpoint(DatabaseEngine database, string principalId,
        PartitionMoveCheckpointBody body, ReadExecutionBudget work)
    {
        if (body.OriginalReceiverIssuanceWitness is not { } witness)
        { return; }
        if (body.Action != PartitionMoveCheckpointAction.Observe || body.ObservedOriginalResult is not null)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        var original = database.ReadPartitionMovementParentPhase(principalId, body.OriginalTransferRequest,
            body.OriginalPhaseCommandId, work);
        RequireReceiverWitnessMac(database, original, witness);
        var reply = NativeSerialization.Deserialize<PartitionMovementTransportReply>(witness.OriginalReplyBytes.Span);
        if (witness.Version != PartitionMoveProtocol.Version || witness.OriginalPhaseCommandId != original.OriginalPhaseCommandId
            || witness.QueryNonce == Guid.Empty || witness.QueryNonce == original.OriginalRequestNonce
            || reply.CommandId != original.OriginalPhaseCommandId || reply.Nonce != witness.QueryNonce
            || reply.OriginalPhaseIdentityDigest != original.OriginalPhaseIdentityDigest
            || !PhysicalOwnerEntryValidation.SameOwner(reply.Receiver, original.OriginalReceiverOwner)
            || !original.OriginalReceiverOwner.VoterIds.Contains(reply.Discovery.VoterId)
            || reply.Discovery.Incarnation != original.OriginalReceiverOwner.Incarnation
            || reply.Discovery.ClusterId != options.Value.ClusterId || reply.Reply.Error is not null)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        var actual = GrainNativePayload.Read<GrainValue>(reply.Reply.Payload).Value as PartitionMovementReceiverIssuanceResult
            ?? throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof);
        RequireReceiverSnapshot(database, original, actual.Snapshot);
        work.CheckResult(actual);
    }

    internal void RequireReceiverWitnessMac(DatabaseEngine database, PartitionMoveParentPhase original,
        PartitionMoveReceiverIssuanceWitness witness)
    {
        if (witness.OriginalReplyBytes.IsEmpty || witness.OriginalReplyBytes.Length > database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        var node = options.Value;
        var key = Convert.FromBase64String(original.OriginalReceiverOwner.PhysicalShardId == node.PhysicalShardId
            && original.OriginalReceiverOwner.Incarnation == database.Store.Identity.Incarnation
            ? node.PeerSecret : node.MembershipAuthority.TrustedGroupPeerSecret
                ?? throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof));
        try
        {
            using var mac = new PartitionMovementMac(key, database.Limits.MaxBatchBytes);
            if (!mac.VerifyReceiverIssue(witness.OriginalReplyBytes.Span, witness.OriginalReplySignature, source: false))
            { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    internal static void RequireReceiverSnapshot(DatabaseEngine database, PartitionMoveParentPhase original,
        PartitionMoveReceiverIssuanceSnapshot snapshot)
    {
        var phase = original.OriginalPhase
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var authorization = original.OriginalAuthorization
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var envelope = new PartitionMovePeerEnvelope(phase.Version, phase.MoveId, phase.Partition, phase.ControlOwner,
            phase.SourcePlacement, phase.DestinationOwner, phase.ControlIntentDigest, phase.Stage, phase.PageOrdinal,
            original.OriginalExpiresAt, original.OriginalRequestNonce, phase.Body, original.OriginalGrant);
        var issued = snapshot.Issuance;
        PartitionMoveReceiverIssuanceValidation.Require(issued, original.OriginalPhaseCommandId, envelope,
            authorization, original.OriginalReceiverOwner, database.Limits.MaxBatchBytes);
        if (issued.ReceiverPrincipalId != PartitionStoreProtocol.AdministratorId
            || issued.IssuanceCommandId != PartitionMoveReceiverIssuanceIdentity.For(original.OriginalPhaseCommandId)
            || snapshot.CurrentReadCut < issued.IssuanceAppliedPosition || snapshot.OriginalResult.Error is not null)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        var actual = snapshot.OriginalResult.Get<PartitionMovePhaseResult>();
        if (actual.Stage != PartitionMovePeerStage.ReceiverIssue || actual.MoveId != issued.MoveId
            || actual.Journal.CommandId != issued.IssuanceCommandId
            || actual.Journal.AppliedPosition != issued.IssuanceAppliedPosition
            || actual.Journal.ControlIntentDigest != issued.OriginalControlIntentDigest
            || !PhysicalOwnerEntryValidation.SameOwner(actual.Journal.PhysicalOwner, issued.ReceiverOwner)
            || !NativeSerialization.Serialize(actual.ReceiverIssuance).AsSpan().SequenceEqual(NativeSerialization.Serialize(issued)))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
    }

    internal void VerifyReceiverSourceCheckpoint(DatabaseEngine database, string principalId,
        PartitionMoveCheckpointBody body, ReadExecutionBudget work)
    {
        if (body.OriginalReceiverSourceWitness is not { } witness)
        { return; }
        if (body.Action != PartitionMoveCheckpointAction.Observe || body.ObservedOriginalResult is not null)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        var original = database.ReadPartitionMovementParentPhase(principalId, body.OriginalTransferRequest,
            body.OriginalPhaseCommandId, work);
        var phase = original.OriginalPhase
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var authorization = original.OriginalAuthorization
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var envelope = new PartitionMovePeerEnvelope(phase.Version, phase.MoveId, phase.Partition,
            phase.ControlOwner, phase.SourcePlacement, phase.DestinationOwner, phase.ControlIntentDigest,
            phase.Stage, phase.PageOrdinal, original.OriginalExpiresAt, original.OriginalRequestNonce,
            phase.Body, original.OriginalGrant);
        if (witness.Version != PartitionMoveProtocol.Version || witness.OriginalPhaseCommandId != original.OriginalPhaseCommandId
            || witness.QueryNonce == Guid.Empty || witness.QueryNonce == original.OriginalRequestNonce
            || original.OriginalPhaseIdentityDigest != PartitionMoveOriginalDispatchIdentity.Digest(original.OriginalPhaseCommandId, envelope))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        var issued = new PartitionMoveReceiverIssueBody(PartitionMoveProtocol.Version, original.OriginalPhaseCommandId,
            envelope, authorization, witness.OriginalReplyBytes, SourcePendingSignature: witness.OriginalReplySignature);
        PartitionMovementReceiverSourceProofVerifier.Require(database, issued, work, options.Value, replicaConfiguration.Value);
        var reply = NativeSerialization.Deserialize<PartitionMovementSourcePendingReply>(witness.OriginalReplyBytes.Span);
        if (reply.Nonce != witness.QueryNonce)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
    }
}
