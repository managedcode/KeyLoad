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
    private const int EmptyCount = 0;

    internal static async Task<ControlledBlobReadResult> ReadAsync(RemoteDocumentClient client,
        NodeOptions options, IOptions<OrleansMembershipOptions> membership, TimeProvider clock,
        RemoteControlledBlobCall call, IControlledBlobWireBorrowObserver? observer, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var body = RemoteDocumentWire.Encode(new RemoteDocumentTransportEnvelope(null, null, call));
        using var sourceMac = RemoteDocumentMac.FromConfiguredSecret(options.PeerSecret);
        var signature = sourceMac.Sign(body, reply: false);
        if (observer is not null)
        { await observer.ObserveAsync(call, body, signature, token).ConfigureAwait(false); }
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
        return reply.ControlledBlob
            ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
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
        if (reply.ControlledBlob is { } result && (result.ReadBytes < EmptyCount || result.ExaminedRecords < EmptyCount
            || result.ReadBytes > call.Request.MaximumReadBytes || result.ExaminedRecords > call.Request.MaximumExaminedRecords))
        { throw Errors.Fail(ErrorCode.BudgetExceeded, RemoteDocumentProtocol.InvalidProof); }
        if (reply.ControlledBlob is { } value && (value.OutcomeValidated != (call.Request.Frame.Purpose == ControlledBlobReadPurpose.Outcome)
            || value.OutcomeValidated && value.NativeValue is not null
            || !ValidValue(call.Request.Frame.Purpose, value.NativeValue)))
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof); }
    }
}
