using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.DocumentStorage;

internal static class RemoteControlledDocumentExchange
{
    private const int SelectedVoter = 0;
    private const int SignatureCount = 1;
    private const int EmptyCount = 0;

    internal static async Task<ControlledDocumentReadResult> ReadAsync(HttpClient http,
        ReplicaMembershipAuthorityAddressPins pins, NodeOptions options,
        IOptions<OrleansMembershipOptions> membership, TimeProvider clock,
        RemoteControlledDocumentCall call, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var body = RemoteDocumentWire.Encode(new RemoteDocumentTransportEnvelope(null, call));
        ControlledDocumentReadResult? result = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var sourceMac = RemoteDocumentMac.FromConfiguredSecret(options.PeerSecret);
            using var request = new HttpRequestMessage(HttpMethod.Post,
                new Uri(new Uri(call.Destination.Endpoints[SelectedVoter]), RemoteDocumentProtocol.Path));
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new(RemoteDocumentProtocol.ContentType);
            request.Headers.Add(RemoteDocumentProtocol.SignatureHeader, sourceMac.Sign(body, reply: false));
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                await ServerFailureObserver.ObserveAsync(async () =>
                { result = await ReadReplyAsync(response, pins, options, membership, clock, call, token).ConfigureAwait(false); },
                    failures).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
    }

    private static async Task<ControlledDocumentReadResult> ReadReplyAsync(HttpResponseMessage response,
        ReplicaMembershipAuthorityAddressPins pins, NodeOptions options,
        IOptions<OrleansMembershipOptions> membership, TimeProvider clock,
        RemoteControlledDocumentCall call, CancellationToken token)
    {
        if (response.StatusCode != System.Net.HttpStatusCode.OK)
        { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable); }
        var bytes = await RemoteDocumentWire.ReadReplyAsync(response.Content, token, call.MaximumReplyBytes).ConfigureAwait(false);
        var signatures = response.Headers.TryGetValues(RemoteDocumentProtocol.SignatureHeader, out var values)
            ? values.Take(SignatureCount + SignatureCount).ToArray() : [];
        using var targetMac = RemoteDocumentMac.FromConfiguredSecret(options.MembershipAuthority.TrustedGroupPeerSecret);
        if (signatures.Length != SignatureCount || !targetMac.Verify(bytes, signatures[SelectedVoter], reply: true))
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof); }
        var reply = NativeSerialization.Deserialize<RemoteDocumentReplyV1>(bytes);
        Require(call, reply, options, membership, clock, token);
        var address = SiloAddress.FromParsableString(reply.EndpointDiscovery.SiloAddress);
        await pins.PinCallerAsync(SelectedVoter, address.Endpoint.Address, token).ConfigureAwait(false);
        if (reply.Error is { } error)
        { throw Errors.Fail(error, reply.SafeDetail ?? RemoteDocumentProtocol.Unavailable); }
        return reply.Controlled ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
    }

    private static void Require(RemoteControlledDocumentCall call, RemoteDocumentReplyV1 reply,
        NodeOptions options, IOptions<OrleansMembershipOptions> membership, TimeProvider clock, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (call.Request.Frame.ExpiresAt <= clock.GetUtcNow() || reply.RequestId != call.RequestId || reply.Nonce != call.Nonce
            || reply.Result is not null || reply.QueryLeaf is not null || reply.ControlledBlob is not null
            || (reply.Error is null) != (reply.Controlled is not null)
            || reply.Error is null && reply.SafeDetail is not null
            || reply.Error is { } error && (!Enum.IsDefined(error) || string.IsNullOrWhiteSpace(reply.SafeDetail))
            || reply.EndpointDiscovery is not { TransportReady: true } discovery
            || discovery.VoterId != call.Destination.Owner.VoterIds[SelectedVoter]
            || discovery.Incarnation != call.Destination.Owner.Incarnation || discovery.ClusterId != options.ClusterId
            || discovery.ApplicationRpcVersion != GrainRoutingProtocol.RequestInterfaceVersion
            || discovery.PeerEnvelopeVersion != ReplicaTransportProtocol.Version
            || discovery.RuntimeJournalReaderContract != StoreReaderContract.RuntimeJournal
            || !ReplicaMembershipAuthorityValidation.CanonicalAddress(discovery.SiloAddress, membership)
            || SiloAddress.FromParsableString(discovery.SiloAddress).Endpoint.Port != MembershipAuthoritySettingsProtocol.NativeSiloPort)
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof); }
        if (reply.Controlled is { } result && (result.ReadBytes < EmptyCount || result.ExaminedRecords < EmptyCount
            || result.ReadBytes > call.Request.MaximumReadBytes || result.ExaminedRecords > call.Request.MaximumExaminedRecords))
        { throw Errors.Fail(ErrorCode.BudgetExceeded, RemoteDocumentProtocol.InvalidProof); }
    }
}
