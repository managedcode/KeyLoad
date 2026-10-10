using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.BlobStorage;

internal static class RemoteControlledBlobExchange
{
    private const int SelectedVoter = 0;
    private const int SignatureCount = 1;
    private const int EmptyCount = 0;

    internal static async Task<ControlledBlobReadResult> ReadAsync(HttpClient http,
        ReplicaMembershipAuthorityAddressPins pins, NodeOptions options,
        IOptions<OrleansMembershipOptions> membership, TimeProvider clock,
        RemoteControlledBlobCall call, IControlledBlobWireBorrowObserver? observer, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var body = RemoteDocumentWire.Encode(new RemoteDocumentTransportEnvelope(null, null, call));
        ControlledBlobReadResult? result = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var sourceMac = RemoteDocumentMac.FromConfiguredSecret(options.PeerSecret);
            using var request = new HttpRequestMessage(HttpMethod.Post,
                new Uri(new Uri(call.Destination.Endpoints[SelectedVoter]), RemoteDocumentProtocol.Path));
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new(RemoteDocumentProtocol.ContentType);
            var signature = sourceMac.Sign(body, reply: false);
            request.Headers.Add(RemoteDocumentProtocol.SignatureHeader, signature);
            if (observer is not null)
            { await observer.ObserveAsync(call, body, signature, token).ConfigureAwait(false); }
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

    private static async Task<ControlledBlobReadResult> ReadReplyAsync(HttpResponseMessage response,
        ReplicaMembershipAuthorityAddressPins pins, NodeOptions options,
        IOptions<OrleansMembershipOptions> membership, TimeProvider clock,
        RemoteControlledBlobCall call, CancellationToken token)
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
        return reply.ControlledBlob ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
    }

    private static bool ValidValue(ControlledBlobReadPurpose purpose, object? value) => purpose switch
    {
        ControlledBlobReadPurpose.Outcome => value is null,
        ControlledBlobReadPurpose.Metadata => value is null or BlobMetadata,
        ControlledBlobReadPurpose.UploadInfo => value is null or BlobUploadInfo,
        ControlledBlobReadPurpose.Range => value is BlobReadResult,
        ControlledBlobReadPurpose.List => value is BlobListPage,
        _ => false
    };

    private static void Require(RemoteControlledBlobCall call, RemoteDocumentReplyV1 reply,
        NodeOptions options, IOptions<OrleansMembershipOptions> membership, TimeProvider clock, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (call.Request.Frame.ExpiresAt <= clock.GetUtcNow() || reply.RequestId != call.RequestId || reply.Nonce != call.Nonce
            || reply.Result is not null || reply.QueryLeaf is not null || reply.Controlled is not null
            || (reply.Error is null) != (reply.ControlledBlob is not null)
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
        if (reply.ControlledBlob is { } result && (result.ReadBytes < EmptyCount || result.ExaminedRecords < EmptyCount
            || result.ReadBytes > call.Request.MaximumReadBytes || result.ExaminedRecords > call.Request.MaximumExaminedRecords))
        { throw Errors.Fail(ErrorCode.BudgetExceeded, RemoteDocumentProtocol.InvalidProof); }
        if (reply.ControlledBlob is { } value && (value.OutcomeValidated != (call.Request.Frame.Purpose == ControlledBlobReadPurpose.Outcome)
            || value.OutcomeValidated && value.NativeValue is not null
            || !ValidValue(call.Request.Frame.Purpose, value.NativeValue)))
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof); }
    }
}
