using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.DocumentStorage;

internal static class RemoteControlledDocumentExchange
{
    private const int SelectedVoter = 0;
    private const int EmptyCount = 0;

    internal static async Task<ControlledDocumentReadResult> ReadAsync(RemoteDocumentClient client,
        NodeOptions options, IOptions<OrleansMembershipOptions> membership, TimeProvider clock,
        RemoteControlledDocumentCall call, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var body = RemoteDocumentWire.Encode(new RemoteDocumentTransportEnvelope(null, call));
        using var sourceMac = RemoteDocumentMac.FromConfiguredSecret(options.PeerSecret);
        var signature = sourceMac.Sign(body, reply: false);
        var original = await client.SendNativeAsync(call.Destination, call.Source,
            RemoteModelNativeChannel.DocumentEnvelope, body, signature, call.Request.Frame.ExpiresAt,
            call.MaximumReplyBytes, token).ConfigureAwait(false);
        RemoteDocumentReplyV1 reply;
        try
        {
            using var targetMac = RemoteDocumentMac.FromConfiguredSecret(options.PhysicalGroupTrust.ForeignPeerSecret);
            if (!targetMac.Verify(original.Payload.Span, original.Signature, reply: true))
            { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof); }
            reply = NativeSerialization.Deserialize<RemoteDocumentReplyV1>(original.Payload.Span);
            Require(call, reply, options, membership, clock, token);
            await client.RequireCurrentReplyAsync(call.Destination, original, reply.EndpointDiscovery.SiloAddress,
                token).ConfigureAwait(false);
        }
        catch (Exception transportFailure) when (original.MayHaveEffects && NativeCqrsBoundaryErrors.IsNonFatal(transportFailure))
        { throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteModelNativeProtocol.Unavailable, transportFailure); }
        if (reply.Error is { } error)
        { throw Errors.Fail(error, reply.SafeDetail ?? RemoteDocumentProtocol.Unavailable); }
        return reply.Controlled
            ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
    }

    private static void Require(RemoteControlledDocumentCall call, RemoteDocumentReplyV1 reply,
        NodeOptions options, IOptions<OrleansMembershipOptions> membership, TimeProvider clock, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (call.Request.Frame.ExpiresAt <= clock.GetUtcNow() || reply.RequestId != call.RequestId || reply.Nonce != call.Nonce
            || reply.Result is not null || reply.QueryLeaf is not null || reply.ControlledBlob is not null
            || (reply.Error is null) != (reply.Controlled is not null)
            || reply.SearchLeaf is not null || reply.QueueTransfer is not null
            || reply.Error is null && reply.SafeDetail is not null
            || reply.Error is { } error && (!Enum.IsDefined(error) || string.IsNullOrWhiteSpace(reply.SafeDetail))
            || reply.EndpointDiscovery is not { TransportReady: true } discovery
            || discovery.VoterId != call.Destination.Owner.VoterIds[SelectedVoter]
            || discovery.Incarnation != call.Destination.Owner.Incarnation || discovery.ClusterId != options.ClusterId
            || discovery.ApplicationRpcVersion != GrainRoutingProtocol.RequestInterfaceVersion
            || discovery.PeerEnvelopeVersion != ReplicaTransportProtocol.Version
            || discovery.RuntimeJournalReaderContract != StoreReaderContract.RuntimeJournal
            || !PhysicalGroupAddress.CanonicalAddress(discovery.SiloAddress, membership))
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof); }
        if (reply.Controlled is { } result && (result.ReadBytes < EmptyCount || result.ExaminedRecords < EmptyCount
            || result.ReadBytes > call.Request.MaximumReadBytes || result.ExaminedRecords > call.Request.MaximumExaminedRecords))
        { throw Errors.Fail(ErrorCode.BudgetExceeded, RemoteDocumentProtocol.InvalidProof); }
    }
}
