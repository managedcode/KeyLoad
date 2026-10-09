using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;
using KeyLoad.Replication;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementTransferProofVerifier
{
    internal static ReadOnlyMemory<byte> Verify(DatabaseEngine database, string principalId,
        ReadOnlyMemory<byte> originalReply, string signature, ReadExecutionBudget work,
        NodeOptions options, ReplicaConfiguration replicaConfiguration, GrainRoutingOptions routing,
        PartitionMovementTransferDataAction action)
    {
        work.Check();
        if (originalReply.IsEmpty || originalReply.Length > database.Limits.MaxBatchBytes
            || string.IsNullOrWhiteSpace(signature))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        database.Store.Read(view =>
        {
            var principal = database.Principal(work.CreateView(view), principalId, database.EvaluationClock.GetUtcNow());
            GrainRequestAuthority.RequireAdministrator(principal);
            return true;
        });
        var control = PhysicalOwnerConfiguredTuples.Control(options, replicaConfiguration).Owner;
        var source = PhysicalOwnerConfiguredTuples.Local(options, replicaConfiguration).Owner;
        RequireTransferAuthorityMac(database, control, source, originalReply.Span, signature, options);
        var reply = NativeSerialization.Deserialize<PartitionMovementTransferAuthorityReply>(originalReply.Span);
        RequireTransferAuthorityReply(database, reply, control, source, options, routing, action);
        var result = GrainNativePayload.Read<GrainValue>(reply.Reply.Payload).Value as PartitionMovementTransferAuthorityResult
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof);
        var authority = result.Authority;
        PartitionMoveTransferAuthorityValidation.Require(authority, database.Limits);
        if (!PhysicalOwnerEntryValidation.SameOwner(authority.Header.ControlOwner, control)
            || !PhysicalOwnerEntryValidation.SameOwner(authority.Header.OriginalSourceOwner!, source)
            || reply.Nonce == authority.CapturePhase.OriginalRequestNonce
            || reply.Nonce == authority.CapturePhase.OriginalCaptureReleaseNonce)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        PartitionMovementRetainedCaptureProofVerifier.Require(database, authority, work, options);
        work.CheckResult(reply);
        return NativeSerialization.Serialize(authority);
    }

    private static void RequireTransferAuthorityReply(DatabaseEngine database, PartitionMovementTransferAuthorityReply reply,
        PhysicalShardRecord control, PhysicalShardRecord source, NodeOptions options, GrainRoutingOptions routing,
        PartitionMovementTransferDataAction action)
    {
        var now = database.EvaluationClock.GetUtcNow();
        if (reply.Version != PartitionMoveProtocol.Version || reply.RequestId == Guid.Empty || reply.Nonce == Guid.Empty
            || reply.ExpiresAt == default
            || action != PartitionMovementTransferDataAction.Close && (reply.ExpiresAt <= now || reply.ExpiresAt > now + routing.RequestLifetime)
            || !PhysicalOwnerEntryValidation.SameOwner(reply.ControlOwner, control)
            || !PhysicalOwnerEntryValidation.SameOwner(reply.SourceOwner, source)
            || reply.Discovery.Incarnation != control.Incarnation || reply.Discovery.ClusterId != options.ClusterId
            || !control.VoterIds.Contains(reply.Discovery.VoterId, StringComparer.Ordinal) || reply.Reply.Error is not null)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
    }

    private static void RequireTransferAuthorityMac(DatabaseEngine database, PhysicalShardRecord control,
        PhysicalShardRecord source, ReadOnlySpan<byte> originalReply, string signature, NodeOptions options)
    {
        var key = Convert.FromBase64String(PhysicalOwnerEntryValidation.SameOwner(control, source)
            ? options.PeerSecret : options.MembershipAuthority.TrustedGroupPeerSecret
                ?? throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof));
        try
        {
            using var mac = new PartitionMovementMac(key, database.Limits.MaxBatchBytes);
            if (!mac.VerifyTransferAuthority(originalReply, signature))
            { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
