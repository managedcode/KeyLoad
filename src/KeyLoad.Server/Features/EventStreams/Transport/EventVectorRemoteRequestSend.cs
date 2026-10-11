using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorRemoteRequestSend
{
    private const int SelectedVoter = 0;

    internal static Task SendAsync(HttpClient http, ReplicaMembershipAuthorityAddressPins pins,
        NodeOptions options, IOptions<OrleansMembershipOptions> membership, TimeProvider clock,
        RegisteredPhysicalOwnerV1 destination, EventVectorRemoteCall originalCall, byte[] body,
        EventVectorRemoteEncoding encoding, ReadExecutionBudget work, List<Exception> failures,
        Action<EventVectorMacCheckedReply, string> completed, CancellationToken originalCancellationToken)
        => ServerFailureObserver.ObserveAsync(async () =>
        {
            using var sourceMac = RemoteDocumentMac.FromConfiguredSecret(options.PeerSecret);
            using var request = new HttpRequestMessage(HttpMethod.Post,
                new Uri(new Uri(destination.Endpoints[SelectedVoter]), EventVectorRemoteProtocol.Path));
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new(RemoteDocumentProtocol.ContentType);
            var signature = sourceMac.Sign(body, reply: false);
            request.Headers.Add(RemoteDocumentProtocol.SignatureHeader, signature);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                    originalCancellationToken).ConfigureAwait(false);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    using var receiverMac = RemoteDocumentMac.FromConfiguredSecret(
                        options.MembershipAuthority.TrustedGroupPeerSecret);
                    await EventVectorRemoteReplyAdmission.ReadAsync(response, originalCall, receiverMac,
                        encoding, work, destination, options, membership, clock, pins, signature,
                        completed, originalCancellationToken).ConfigureAwait(false);
                }, failures).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures);
}
