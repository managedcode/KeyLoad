using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Verifies the original purpose MAC and binds its exact signed reply to the addressed native silo.</summary>
internal sealed class PartitionMovementTransportReplyVerifier(PartitionMovementTransportContext context)
{
    internal enum ReceiverReplyPurpose
    {
        Effect, Outcome, ReceiverIssue, RetireCancellation
    }

    internal PartitionMovementAuthenticatedReply ReadReplyMacPurpose(PartitionMovementGrainServiceReply response,
        bool local, ReceiverReplyPurpose purpose)
    {
        if (response.Payload.IsEmpty || response.Payload.Length > context.Partition.Database.Limits.MaxBatchBytes
            || string.IsNullOrEmpty(response.Signature))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof);
        }
        var body = response.Payload.ToArray();
        var key = Convert.FromBase64String(PartitionMovementNativeDiscoveryIdentity.ReplySecret(
            context.Options, context.Partition.Configuration, local));
        try
        {
            using var mac = new PartitionMovementMac(key, context.Partition.Database.Limits.MaxBatchBytes);
            if (!(purpose switch
            {
                ReceiverReplyPurpose.Effect => mac.Verify(body, response.Signature, reply: true),
                ReceiverReplyPurpose.Outcome => mac.VerifyOutcome(body, response.Signature, reply: true),
                ReceiverReplyPurpose.ReceiverIssue => mac.VerifyReceiverIssue(body, response.Signature, source: false),
                ReceiverReplyPurpose.RetireCancellation => mac.VerifyRetireCancellationReply(body, response.Signature),
                _ => false
            }))
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof);
            }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        return new(NativeSerialization.Deserialize<PartitionMovementTransportReply>(body), body, response.Signature);
    }

    internal Task VerifyReplyAsync(int index, RegisteredPhysicalOwnerV1 receiver,
        PartitionMovementTransportRequest request, PartitionMovementTransportReply reply, SiloAddress target,
        string expectedPhaseIdentityDigest, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var discovery = reply.Discovery;
        if (request.Envelope.ExpiresAt <= context.Clock.GetUtcNow() || reply.CommandId != request.CommandId
            || reply.Nonce != request.Envelope.Nonce || reply.Reply is null
            || reply.OriginalPhaseIdentityDigest != expectedPhaseIdentityDigest
            || !PhysicalOwnerEntryValidation.SameOwner(reply.Receiver, receiver.Owner)
            || discovery is not { TransportReady: true }
            || discovery.VoterId != receiver.Owner.VoterIds[index]
            || discovery.ClusterId != context.Options.ClusterId || discovery.Incarnation != receiver.Owner.Incarnation
            || discovery.ApplicationRpcVersion != GrainRoutingProtocol.RequestInterfaceVersion
            || discovery.PeerEnvelopeVersion != ReplicaTransportProtocol.Version
            || discovery.RuntimeJournalReaderContract != StoreReaderContract.RuntimeJournal
            || discovery.SiloAddress != target.ToParsableString()
            || !PhysicalGroupAddress.CanonicalAddress(discovery.SiloAddress, context.Membership))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof);
        }
        PartitionMovementTransportReplyValidation.RequireValue(reply.Reply);
        return Task.CompletedTask;
    }
}
