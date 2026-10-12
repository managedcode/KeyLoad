using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Storage;

namespace KeyLoad.Server.Features.GraphTraversal;

internal static class RemoteGraphNativeReplyVerification
{
    internal static async Task<RemoteGraphNativeReplyV1> VerifyAsync(RemoteGraphNativeClient client,
        PartitionHost partition, NodeOptions options, TimeProvider clock, RemoteGraphNativeRequestV1 request,
        RemoteGraphNativeReplyV1 received, ReadExecutionBudget work, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        work.Check();
        if (request.ExpiresAt <= clock.GetUtcNow()
            || !RemoteGraphNativeIdentity.ReplyShape(received, Math.Min(partition.Database.Limits.MaxBatchBytes, work.MaximumResultBytes)))
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphNativeProtocol.InvalidProof); }
        work.ChargeBytes(received.Payload.Length);
        var reply = received with { Payload = received.Payload.ToArray() };
        using var mac = RemoteGraphNativeMac.Create(options, partition, request.DestinationOwner);
        var discovery = reply.ReceiverDiscovery;
        if (reply.RequestId != request.RequestId || reply.Nonce != request.Nonce || !mac.VerifyRemoteGraphReply(reply)
            || !discovery.TransportReady || discovery.VoterId != request.DestinationVoter
            || discovery.SiloAddress != request.DestinationSiloAddress || discovery.ClusterId != options.ClusterId
            || discovery.Incarnation != request.DestinationOwner.Owner.Incarnation
            || discovery.ApplicationRpcVersion != GrainRoutingProtocol.RequestInterfaceVersion
            || discovery.PeerEnvelopeVersion != ReplicaTransportProtocol.Version
            || discovery.RuntimeJournalReaderContract != StoreReaderContract.RuntimeJournal)
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphNativeProtocol.InvalidProof); }
        await client.RequireCurrentAsync(request, token).ConfigureAwait(false);
        work.Check();
        token.ThrowIfCancellationRequested();
        return reply;
    }
}
