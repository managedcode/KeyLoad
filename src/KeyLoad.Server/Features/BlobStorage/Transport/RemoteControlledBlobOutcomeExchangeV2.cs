using KeyLoad.Clustering;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.BlobStorage;

internal static class RemoteControlledBlobOutcomeExchangeV2
{
    private const int SelectedVoter = 0;
    private const int EmptyCount = 0;

    internal static async Task<ControlledBlobReadResult> ReadAsync(RemoteDocumentClient client,
        NodeOptions options, IOptions<OrleansMembershipOptions> membership, TimeProvider clock,
        RemoteControlledBlobOutcomeCallV2 call, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var originalCall = call.Original;
        var body = RemoteDocumentWire.Encode(call);
        using var sourceMac = RemoteDocumentMac.FromConfiguredSecret(options.PeerSecret);
        var signature = sourceMac.Sign(body, reply: false);
        var original = await client.SendNativeAsync(originalCall.Destination, originalCall.Source,
            RemoteModelNativeChannel.ResourceLifetimeBlobOutcome, body, signature, originalCall.Request.Frame.ExpiresAt,
            originalCall.MaximumReplyBytes, token).ConfigureAwait(false);
        RemoteControlledBlobOutcomeReplyV2 reply;
        try
        {
            using var targetMac = RemoteDocumentMac.FromConfiguredSecret(options.PhysicalGroupTrust.ForeignPeerSecret);
            if (!targetMac.Verify(original.Payload.Span, original.Signature, reply: true))
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof);
            }
            reply = NativeSerialization.Deserialize<RemoteControlledBlobOutcomeReplyV2>(original.Payload.Span);
            Require(originalCall, reply, options, membership, clock, token);
            await client.RequireCurrentReplyAsync(originalCall.Destination, original, reply.EndpointDiscovery.SiloAddress,
                token).ConfigureAwait(false);
        }
        catch (Exception transportFailure) when (original.MayHaveEffects && NativeCqrsBoundaryErrors.IsNonFatal(transportFailure))
        {
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteModelNativeProtocol.Unavailable, transportFailure);
        }
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, reply.SafeDetail ?? RemoteDocumentProtocol.Unavailable);
        }
        return reply.Result
            ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
    }

    private static void Require(RemoteControlledBlobCall call, RemoteControlledBlobOutcomeReplyV2 reply,
        NodeOptions options, IOptions<OrleansMembershipOptions> membership, TimeProvider clock, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (call.Request.Frame.ExpiresAt <= clock.GetUtcNow() || reply.RequestId != call.RequestId || reply.Nonce != call.Nonce
            || (reply.Error is null) != (reply.Result is not null)
            || reply.Error is null && reply.SafeDetail is not null
            || reply.Error is { } error && (!Enum.IsDefined(error) || string.IsNullOrWhiteSpace(reply.SafeDetail))
            || reply.EndpointDiscovery is not { TransportReady: true } discovery
            || discovery.VoterId != call.Destination.Owner.VoterIds[SelectedVoter]
            || discovery.Incarnation != call.Destination.Owner.Incarnation || discovery.ClusterId != options.ClusterId
            || discovery.ApplicationRpcVersion != GrainRoutingProtocol.RequestInterfaceVersion
            || discovery.PeerEnvelopeVersion != ReplicaTransportProtocol.Version
            || discovery.RuntimeJournalReaderContract != StoreReaderContract.RuntimeJournal
            || !PhysicalGroupAddress.CanonicalAddress(discovery.SiloAddress, membership))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof);
        }
        if (reply.Result is { } result && (result.ReadBytes < EmptyCount || result.ExaminedRecords < EmptyCount
            || result.ReadBytes > call.Request.MaximumReadBytes || result.ExaminedRecords > call.Request.MaximumExaminedRecords))
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, RemoteDocumentProtocol.InvalidProof);
        }
        if (reply.Result is { } value && (!value.OutcomeValidated || value.NativeValue is not null))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof);
        }
    }
}
