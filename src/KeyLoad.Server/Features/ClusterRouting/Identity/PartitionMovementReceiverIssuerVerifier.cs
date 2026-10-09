using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Owns the original issuer native proof checks with the same configured authority and operation work.</summary>
internal sealed class PartitionMovementReceiverIssuerVerifier(IOptions<NodeOptions> options, IOptions<ReplicaConfiguration> replicaConfiguration)
{
    internal void RequireNativeIssuerPacket(DatabaseEngine database, PartitionMoveReceiverIssueBody body,
        ReadExecutionBudget work)
    {
        var raw = body.OriginalIssuerRequestBytes;
        if (raw.IsEmpty || raw.Length > database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        RequireIssuerPacketMac(database, body.OriginalEnvelope, raw, body.OriginalIssuerRequestSignature);
        var actual = NativeSerialization.Deserialize<PartitionMovementReceiverIssueRequest>(raw.Span);
        if (actual.Version != PartitionMoveProtocol.Version || actual.OriginalPhaseCommandId != body.OriginalPhaseCommandId
            || actual.IssuanceCommandId != PartitionMoveReceiverIssuanceIdentity.For(body.OriginalPhaseCommandId)
            || actual.IssuanceNonce != PartitionMovementReceiverIssueNonce.For(body.OriginalPhaseCommandId, actual.SourceWitness.QueryNonce)
            || actual.IssuanceNonce == body.OriginalEnvelope.Nonce || actual.IssuanceNonce == actual.SourceWitness.QueryNonce
            || actual.SourceProofCheckpointReceipt.CommandId == Guid.Empty || actual.SourceProofCheckpointReceipt.AppliedPosition <= PartitionMovementProtocol.NoAppliedPosition
            || !PhysicalOwnerEntryValidation.SameOwner(actual.SourceProofCheckpointReceipt.PhysicalOwner, body.OriginalEnvelope.ControlOwner)
            || !body.OriginalEnvelope.ControlOwner.VoterIds.Contains(actual.CallerVoter)
            || string.IsNullOrWhiteSpace(actual.CallerSiloAddress)
            || !NativeSerialization.Serialize(actual.OriginalEnvelope).AsSpan().SequenceEqual(NativeSerialization.Serialize(body.OriginalEnvelope))
            || !NativeSerialization.Serialize(actual.OriginalAuthorization).AsSpan().SequenceEqual(NativeSerialization.Serialize(body.OriginalAuthorization))
            || actual.SourceWitness.Version != PartitionMoveProtocol.Version
            || actual.SourceWitness.OriginalPhaseCommandId != body.OriginalPhaseCommandId
            || actual.SourceWitness.QueryNonce == Guid.Empty || actual.SourceWitness.QueryNonce == body.OriginalEnvelope.Nonce
            || !actual.SourceWitness.OriginalReplyBytes.Span.SequenceEqual(body.SourcePendingProof.Span)
            || actual.SourceWitness.OriginalReplySignature != body.SourcePendingSignature)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        work.Check();
    }

    internal void RequireIssuerPacketMac(DatabaseEngine database, PartitionMovePeerEnvelope original,
        ReadOnlyMemory<byte> raw, string signature)
    {
        var node = options.Value;
        var key = Convert.FromBase64String(PartitionMovementControlProofKey.Resolve(node,
            replicaConfiguration.Value, original.ControlOwner));
        try
        {
            using var mac = new PartitionMovementMac(key, database.Limits.MaxBatchBytes);
            if (!mac.VerifyReceiverIssueRequest(raw.Span, signature, query: false))
            { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    internal string VerifyReceiverIssue(DatabaseEngine database, string principalId,
        ReadOnlyMemory<byte> ownedBody, ReadExecutionBudget work)
    {
        RequireReceiverAdministrator(database, principalId, work);
        if (ownedBody.IsEmpty || ownedBody.Length > database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMovementProtocol.InvalidProof); }
        var body = NativeSerialization.Deserialize<PartitionMoveReceiverIssueBody>(ownedBody.Span);
        if (body.ActualReceiverPrincipalId != principalId || body.ActualReceiverPolicyEpoch <= PartitionMovementProtocol.UnissuedPolicyEpoch)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        RequireNativeIssuerPacket(database, body, work);
        PartitionMovementReceiverSourceProofVerifier.Require(database, body, work, options.Value, replicaConfiguration.Value);
        work.Check();
        return Convert.ToHexStringLower(SHA256.HashData(ownedBody.Span));
    }

    internal static void RequireReceiverAdministrator(DatabaseEngine database, string principalId, ReadExecutionBudget work)
    {
        work.Check();
        if (principalId != PartitionStoreProtocol.AdministratorId)
        { throw Errors.Fail(ErrorCode.PermissionDenied, PartitionMovementProtocol.InvalidProof); }
        database.Store.Read(view =>
        {
            var principal = database.Principal(work.CreateView(view), principalId, database.EvaluationClock.GetUtcNow());
            if (!principal.ClusterAdministrator)
            { throw Errors.Fail(ErrorCode.PermissionDenied, PartitionMovementProtocol.InvalidProof); }
            return true;
        });
        work.Check();
    }
}
