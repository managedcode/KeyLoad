using KeyLoad.Core.Features.Messaging;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Messaging;

internal static class RemoteTransferPeerExchange
{
    internal static async Task<RemoteQueueTransferPeerResult> SendAsync(HttpClient http,
        ReplicaMembershipAuthorityAddressPins pins, NodeOptions options,
        IOptions<OrleansMembershipOptions> membership, TimeProvider clock,
        RemoteQueueTransferPeerCall call, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        RemoteTransferPeerShape.Require(call);
        var body = RemoteDocumentWire.Encode(new RemoteDocumentTransportEnvelope(null, null, QueueTransfer: call));
        RemoteQueueTransferPeerResult? result = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var mac = RemoteDocumentMac.FromConfiguredSecret(options.PeerSecret);
            using var request = new HttpRequestMessage(HttpMethod.Post,
                new Uri(new Uri(call.DestinationOwner.Endpoints[RemoteTransferPeerProtocol.SelectedVoter]), RemoteDocumentProtocol.Path));
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new(RemoteDocumentProtocol.ContentType);
            request.Headers.Add(RemoteDocumentProtocol.SignatureHeader, mac.Sign(body, reply: false));
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                await ServerFailureObserver.ObserveAsync(async () =>
                { result = await ReadAsync(response, pins, options, membership, clock, call, token).ConfigureAwait(false); },
                    failures).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, RemoteTransferPeerProtocol.Invalid);
    }

    private static async Task<RemoteQueueTransferPeerResult> ReadAsync(HttpResponseMessage response,
        ReplicaMembershipAuthorityAddressPins pins, NodeOptions options,
        IOptions<OrleansMembershipOptions> membership, TimeProvider clock,
        RemoteQueueTransferPeerCall call, CancellationToken token)
    {
        if (response.StatusCode != System.Net.HttpStatusCode.OK)
        { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteTransferPeerProtocol.Unavailable); }
        var encoded = await RemoteDocumentWire.ReadReplyAsync(response.Content, token, call.MaximumReplyBytes).ConfigureAwait(false);
        var signatures = response.Headers.TryGetValues(RemoteDocumentProtocol.SignatureHeader, out var values)
            ? values.Take(RemoteTransferPeerProtocol.SignatureCount + RemoteTransferPeerProtocol.SignatureCount).ToArray() : [];
        using var mac = RemoteDocumentMac.FromConfiguredSecret(options.MembershipAuthority.TrustedGroupPeerSecret);
        if (signatures.Length != RemoteTransferPeerProtocol.SignatureCount
            || !mac.Verify(encoded, signatures[RemoteTransferPeerProtocol.SelectedVoter], reply: true))
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteTransferPeerProtocol.Invalid); }
        var reply = NativeSerialization.Deserialize<RemoteDocumentReplyV1>(encoded);
        RemoteTransferPeerReplyValidation.Require(call, reply, options, membership, clock, token);
        var address = SiloAddress.FromParsableString(reply.EndpointDiscovery.SiloAddress);
        await pins.PinCallerAsync(RemoteTransferPeerProtocol.SelectedVoter, address.Endpoint.Address, token).ConfigureAwait(false);
        if (reply.Error is { } error)
        { throw Errors.Fail(error, reply.SafeDetail ?? RemoteTransferPeerProtocol.Unavailable); }
        return reply.QueueTransfer ?? throw Errors.Fail(ErrorCode.Corruption, RemoteTransferPeerProtocol.Invalid);
    }
}
