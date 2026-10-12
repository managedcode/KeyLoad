using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorRemoteRequestSend
{
    internal static Task SendAsync(RemoteDocumentClient client,
        NodeOptions options, IOptions<OrleansMembershipOptions> membership, TimeProvider clock,
        RegisteredPhysicalOwnerV1 destination, EventVectorRemoteCall originalCall, byte[] body,
        EventVectorRemoteEncoding encoding, ReadExecutionBudget work, List<Exception> failures,
        Action<EventVectorMacCheckedReply, string> completed, CancellationToken originalCancellationToken)
        => ServerFailureObserver.ObserveAsync(async () =>
        {
            using var sourceMac = RemoteDocumentMac.FromConfiguredSecret(options.PeerSecret);
            var signature = sourceMac.Sign(body, reply: false);
            var reply = await client.SendNativeAsync(destination, originalCall.Source,
                RemoteModelNativeChannel.EventVector, body, signature, originalCall.ExpiresAt,
                encoding.MaximumBytes, originalCancellationToken).ConfigureAwait(false);
            using var receiverMac = RemoteDocumentMac.FromConfiguredSecret(options.PhysicalGroupTrust.ForeignPeerSecret);
            await EventVectorRemoteReplyAdmission.ReadAsync(reply, originalCall,
                receiverMac, encoding, work, destination, options, membership, clock, client,
                signature, completed, originalCancellationToken).ConfigureAwait(false);
        }, failures);
}
