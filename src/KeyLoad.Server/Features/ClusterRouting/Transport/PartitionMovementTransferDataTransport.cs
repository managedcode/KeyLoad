using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;
using KeyLoad.Storage;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Reads original bounded transfer pages through the actual native grain service.</summary>
internal sealed class PartitionMovementTransferDataTransport(PartitionMovementTransportContext context)
{
    internal async Task<PartitionMovementTransferDataReply> ExchangeTransferDataAsync(int index,
        RegisteredPhysicalOwnerV1 source, PartitionMovementTransferDataRequest original, byte[] body,
        CancellationToken cancellationToken)
    {
        var address = await context.ResolveAsync(source, index, readOnly: false, cancellationToken).ConfigureAwait(false);
        var signature = Sign(body);
        var response = await context.ExchangeAsync(address, source.Owner.VoterIds[index],
            PartitionMovementGrainServiceChannel.TransferData, query: true, body, signature,
            readOnly: false, cancellationToken).ConfigureAwait(false);
        var reply = ReadReply(response, source);
        VerifyReply(index, source, original, reply, address, cancellationToken);
        return reply;
    }

    private string Sign(byte[] body)
    {
        var key = Convert.FromBase64String(context.Options.PeerSecret);
        try
        {
            using var mac = new PartitionMovementMac(key, context.Partition.Database.Limits.MaxBatchBytes);
            return mac.SignTransferData(body, reply: false);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private PartitionMovementTransferDataReply ReadReply(PartitionMovementGrainServiceReply response, RegisteredPhysicalOwnerV1 source)
    {
        if (response.Payload.IsEmpty || response.Payload.Length > context.Partition.Database.Limits.MaxBatchBytes
            || string.IsNullOrEmpty(response.Signature))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        var body = response.Payload.ToArray();
        var key = Convert.FromBase64String(PartitionMovementNativeDiscoveryIdentity.Secret(
            context.Options, context.Partition.Configuration, source));
        try
        {
            using var mac = new PartitionMovementMac(key, context.Partition.Database.Limits.MaxBatchBytes);
            if (!mac.VerifyTransferData(body, response.Signature, reply: true))
            { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        return NativeSerialization.Deserialize<PartitionMovementTransferDataReply>(body);
    }

    private void VerifyReply(int index, RegisteredPhysicalOwnerV1 source, PartitionMovementTransferDataRequest original,
        PartitionMovementTransferDataReply reply, SiloAddress target, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var discovery = reply.Discovery;
        if (original.ExpiresAt <= context.Clock.GetUtcNow() || reply.RequestId != original.RequestId
            || reply.Nonce != original.Nonce || reply.Reply is null
            || !PhysicalOwnerEntryValidation.SameOwner(reply.SourceOwner, source.Owner)
            || discovery is not { TransportReady: true }
            || discovery.VoterId != source.Owner.VoterIds[index] || discovery.ClusterId != context.Options.ClusterId
            || discovery.Incarnation != source.Owner.Incarnation
            || discovery.ApplicationRpcVersion != GrainRoutingProtocol.RequestInterfaceVersion
            || discovery.PeerEnvelopeVersion != ReplicaTransportProtocol.Version
            || discovery.RuntimeJournalReaderContract != StoreReaderContract.RuntimeJournal
            || discovery.SiloAddress != target.ToParsableString()
            || !PhysicalGroupAddress.CanonicalAddress(discovery.SiloAddress, context.Membership))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        PartitionMovementTransportReplyValidation.RequireValue(reply.Reply);
    }
}
