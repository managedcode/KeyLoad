using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Owns the original retire native proof checks with the same configured authority and operation work.</summary>
internal sealed class PartitionMovementRetireCancellationVerifier(IOptions<NodeOptions> options, IOptions<ReplicaConfiguration> replicaConfiguration)
{
    internal string VerifyRetireCancellation(DatabaseEngine database, string principalId,
        ReadOnlyMemory<byte> ownedBody, ReadExecutionBudget work)
    {
        PartitionMovementReceiverIssuerVerifier.RequireReceiverAdministrator(database, principalId, work);
        RequireRetireCancellationBytes(database, ownedBody);
        var body = NativeSerialization.Deserialize<PartitionMoveRetireCancellationBody>(ownedBody.Span);
        RequireRetireCancellationRequestMac(database, body.OriginalEnvelope,
            body.OriginalRequestBytes, body.OriginalRequestSignature, query: false);
        var request = NativeSerialization.Deserialize<PartitionMovementRetireCancellationRequest>(body.OriginalRequestBytes.Span);
        if (body.ActualReceiverPrincipalId != principalId || body.ActualReceiverPolicyEpoch <= PartitionMoveProtocol.EmptyCount
            || request.Version != body.Version || request.CancellationCommandId != body.CancellationCommandId
            || request.OriginalPhaseCommandId != body.OriginalPhaseCommandId
            || request.CleanupGeneration != body.CleanupGeneration || request.CancellationExpiresAt != body.CancellationExpiresAt
            || request.CancellationExpiresAt <= database.EvaluationClock.GetUtcNow()
            || request.CancellationNonce == Guid.Empty || request.CancellationNonce == body.OriginalEnvelope.Nonce
            || !SameRetireCancellationValue(request.OriginalEnvelope, body.OriginalEnvelope)
            || !SameRetireCancellationValue(request.OriginalAuthorization, body.OriginalAuthorization)
            || !SameRetireCancellationValue(request.SourceWitness, body.OriginalSourceWitness))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        PartitionMovementRetireCancellationSourceProofVerifier.Require(database, body, work,
            options.Value, replicaConfiguration.Value);
        work.Check();
        return Convert.ToHexStringLower(SHA256.HashData(ownedBody.Span));
    }

    internal string VerifyRetireCancellationQuery(DatabaseEngine database, string principalId,
        ReadOnlyMemory<byte> ownedBody, ReadExecutionBudget work)
    {
        PartitionMovementReceiverIssuerVerifier.RequireReceiverAdministrator(database, principalId, work);
        RequireRetireCancellationBytes(database, ownedBody);
        var body = NativeSerialization.Deserialize<PartitionMoveRetireCancellationReadBody>(ownedBody.Span);
        RequireRetireCancellationRequestMac(database, body.OriginalEnvelope,
            body.OriginalRequestBytes, body.OriginalRequestSignature, query: true);
        var query = NativeSerialization.Deserialize<PartitionMovementRetireCancellationQuery>(body.OriginalRequestBytes.Span);
        if (query.Version != body.Version || query.CancellationCommandId != body.CancellationCommandId
            || query.OriginalPhaseCommandId != body.OriginalPhaseCommandId || query.QueryExpiresAt != body.QueryExpiresAt
            || query.QueryExpiresAt <= database.EvaluationClock.GetUtcNow()
            || query.QueryNonce == Guid.Empty || query.QueryNonce == body.OriginalEnvelope.Nonce
            || !SameRetireCancellationValue(query.OriginalEnvelope, body.OriginalEnvelope)
            || !SameRetireCancellationValue(query.OriginalAuthorization, body.OriginalAuthorization))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        work.Check();
        return Convert.ToHexStringLower(SHA256.HashData(ownedBody.Span));
    }

    internal static bool SameRetireCancellationValue<T>(T left, T right)
        => NativeSerialization.Serialize(left).AsSpan().SequenceEqual(NativeSerialization.Serialize(right));

    internal static void RequireRetireCancellationBytes(DatabaseEngine database, ReadOnlyMemory<byte> bytes)
    {
        if (bytes.IsEmpty || bytes.Length > database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
    }

    internal void RequireRetireCancellationRequestMac(DatabaseEngine database, PartitionMovePeerEnvelope original,
        ReadOnlyMemory<byte> bytes, string signature, bool query)
    {
        RequireRetireCancellationBytes(database, bytes);
        var key = Convert.FromBase64String(PartitionMovementControlProofKey.Resolve(options.Value,
            replicaConfiguration.Value, original.ControlOwner));
        try
        {
            using var mac = new PartitionMovementMac(key, database.Limits.MaxBatchBytes);
            if (!mac.VerifyRetireCancellationRequest(bytes.Span, signature, query))
            { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    internal void VerifyRetireCancellationCheckpoint(DatabaseEngine database, string principalId,
        PartitionMoveCheckpointBody body, ReadExecutionBudget work)
    {
        if (body.Action is not (PartitionMoveCheckpointAction.AdmitRetireCancellation
            or PartitionMoveCheckpointAction.ObserveRetireCancellation))
        {
            if (body.RetireCancellation is not null || body.RetireCancellationAttempt is not null)
            { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
            return;
        }
        var original = database.ReadPartitionMovementParentPhase(principalId, body.OriginalTransferRequest,
            body.OriginalPhaseCommandId, work);
        var envelope = PartitionMovementClient.RetireCancellationOriginalEnvelope(original);
        if (body.Action == PartitionMoveCheckpointAction.AdmitRetireCancellation)
        {
            var attempt = body.RetireCancellationAttempt
                ?? throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof);
            VerifyRetireCancellationAttempt(database, original, envelope, attempt, work);
            return;
        }
        var witness = body.RetireCancellation
            ?? throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof);
        VerifyRetireCancellationWitness(database, original, witness, work);
    }

    internal void VerifyRetireCancellationAttempt(DatabaseEngine database, PartitionMoveParentPhase original,
        PartitionMovePeerEnvelope envelope, PartitionMoveRetireCancellationAttempt attempt, ReadExecutionBudget work)
    {
        RequireRetireCancellationRequestMac(database, envelope, attempt.OriginalRequestBytes,
            attempt.OriginalRequestSignature, query: false);
        var first = NativeSerialization.Deserialize<PartitionMovementRetireCancellationRequest>(attempt.OriginalRequestBytes.Span);
        if (first.Version != attempt.Version || first.CancellationCommandId != attempt.CancellationCommandId
            || first.CancellationNonce != attempt.CancellationNonce || first.CancellationExpiresAt != attempt.CancellationExpiresAt
            || first.CancellationExpiresAt <= database.EvaluationClock.GetUtcNow()
            || first.OriginalPhaseCommandId != original.OriginalPhaseCommandId
            || first.CleanupGeneration != original.CleanupGeneration
            || !SameRetireCancellationValue(first.OriginalEnvelope, envelope)
            || !SameRetireCancellationValue(first.OriginalAuthorization, original.OriginalAuthorization))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        var value = new PartitionMoveRetireCancellationBody(first.Version, first.CancellationCommandId,
            first.OriginalPhaseCommandId, first.OriginalEnvelope, first.OriginalAuthorization,
            first.CleanupGeneration, attempt.OriginalRequestBytes, attempt.OriginalRequestSignature,
            first.CancellationExpiresAt, OriginalSourceWitness: first.SourceWitness);
        PartitionMovementRetireCancellationSourceProofVerifier.Require(database, value, work,
            options.Value, replicaConfiguration.Value);
    }

    internal void VerifyRetireCancellationWitness(DatabaseEngine database, PartitionMoveParentPhase original,
        PartitionMoveRetireCancellationWitness witness, ReadExecutionBudget work)
    {
        RequireRetireCancellationBytes(database, witness.OriginalReplyBytes);
        var node = options.Value;
        var owner = original.OriginalReceiverOwner;
        var key = Convert.FromBase64String(owner.PhysicalShardId == node.PhysicalShardId
            && owner.Incarnation == database.Store.Identity.Incarnation ? node.PeerSecret
                : node.MembershipAuthority.TrustedGroupPeerSecret
                    ?? throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof));
        try
        {
            using var mac = new PartitionMovementMac(key, database.Limits.MaxBatchBytes);
            if (!mac.VerifyRetireCancellationReply(witness.OriginalReplyBytes.Span, witness.OriginalReplySignature))
            { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        var reply = NativeSerialization.Deserialize<PartitionMovementTransportReply>(witness.OriginalReplyBytes.Span);
        if (reply.CommandId != witness.Cancellation.CancellationCommandId || reply.Nonce == Guid.Empty
            || reply.Nonce == original.OriginalRequestNonce || reply.OriginalPhaseIdentityDigest != original.OriginalPhaseIdentityDigest
            || !KeyLoad.Core.Features.ClusterRouting.Validation.PhysicalOwnerEntryValidation.SameOwner(reply.Receiver, owner)
            || !owner.VoterIds.Contains(reply.Discovery.VoterId) || reply.Discovery.Incarnation != owner.Incarnation
            || reply.Discovery.ClusterId != node.ClusterId || reply.Reply.Error is not null)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        var result = KeyLoad.Orleans.GrainNativePayload.Read<KeyLoad.Orleans.GrainValue>(reply.Reply.Payload).Value
            as KeyLoad.Orleans.PartitionMovementRetireCancellationOutcomeResult
            ?? throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof);
        var snapshot = result.Snapshot;
        if (snapshot.Cancellation is null || snapshot.NativeCancellationResult is null
            || snapshot.NativeCancellationResult.Error is not null
            || snapshot.CurrentReadCut < witness.Cancellation.CancellationReceipt.AppliedPosition
            || !SameRetireCancellationValue(snapshot.Cancellation, witness.Cancellation))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        var actual = snapshot.NativeCancellationResult.Get<PartitionMovePhaseResult>();
        if (actual.Stage != PartitionMovePeerStage.RetireCancel || actual.MoveId != original.MoveId
            || !SameRetireCancellationValue(actual.RetireCancellation, witness.Cancellation)
            || !SameRetireCancellationValue(actual.Journal, witness.Cancellation.CancellationReceipt))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        work.CheckResult(result);
    }
}
