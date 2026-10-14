using KeyLoad.Clustering;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Server.Features.BlobStorage;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.QueryExecution;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class RemoteDocumentClient : IAsyncDisposable
{
    private const int SelectedVoter = 0;
    private const long EmptyApplied = 0;
    private readonly NodeOptions options;
    private readonly IOptions<OrleansMembershipOptions> membership;
    private readonly TimeProvider clock;
    private readonly IControlledBlobWireBorrowObserver? wireObserver;
    private readonly Func<RemoteModelNativeClient> borrowNative;
    private RemoteModelNativeClient? native;

    internal RemoteDocumentClient(IOptions<NodeOptions> options,
        IOptions<OrleansMembershipOptions> membership, TimeProvider clock,
        Func<RemoteModelNativeClient> borrowNative, IControlledBlobWireBorrowObserver? wireObserver = null)
    {
        this.options = options.Value;
        this.membership = membership;
        this.clock = clock;
        this.borrowNative = borrowNative;
        this.wireObserver = wireObserver;
    }

    internal Task<RemoteModelAuthenticatedReply> SendNativeAsync(RegisteredPhysicalOwnerV1 destination,
        PhysicalShardRecord source, RemoteModelNativeChannel channel, ReadOnlyMemory<byte> payload,
        string signature, DateTimeOffset expiresAt, long maximumReplyBytes, CancellationToken token)
        => Native.SendAsync(destination, source, channel, payload, signature, expiresAt, maximumReplyBytes, token);

    internal async Task RequireCurrentReplyAsync(RegisteredPhysicalOwnerV1 destination,
        RemoteModelAuthenticatedReply original, string innerAddress, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (!PhysicalGroupAddress.CanonicalAddress(innerAddress, membership)
                || !string.Equals(original.DispatchSiloAddress, innerAddress, StringComparison.Ordinal))
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof);
            }
            await Native.RequireCurrentAsync(destination, original.DispatchSiloAddress, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }
        catch (Exception error) when (original.MayHaveEffects && NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteModelNativeProtocol.Unavailable, error);
        }
    }

    internal async Task<OwnedDocumentReadResultV1> ReadAsync(RemoteDocumentCallV1 call,
        CancellationToken cancellationToken)
    {
        var reply = await ReadReplyAsync(call, cancellationToken).ConfigureAwait(false);
        return reply.Result ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
    }

    internal async Task<PartitionQueryLeafResultV1> ReadLeafAsync(RemoteDocumentCallV1 call,
        CancellationToken cancellationToken)
    {
        var reply = await ReadReplyAsync(call, cancellationToken).ConfigureAwait(false);
        return reply.QueryLeaf ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
    }

    internal async Task<KeyLoad.Query.Features.QueryExecution.DistributedSearchLeafResultV1> ReadSearchLeafAsync(
        RemoteDocumentCallV1 call, CancellationToken cancellationToken)
    {
        var reply = await ReadReplyAsync(call, cancellationToken).ConfigureAwait(false);
        return reply.SearchLeaf ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
    }

    internal Task<ControlledDocumentReadResult> ReadControlledAsync(RemoteControlledDocumentCall call,
        CancellationToken cancellationToken)
        => RemoteControlledDocumentExchange.ReadAsync(this, options, membership, clock, call, cancellationToken);

    internal Task<ControlledBlobReadResult> ReadControlledBlobAsync(RemoteControlledBlobCall call,
        CancellationToken cancellationToken)
        => RemoteControlledBlobExchange.ReadAsync(this, options, membership, clock, call, wireObserver, cancellationToken);

    internal Task<KeyLoad.Server.Features.EventStreams.EventVectorAuthenticatedExchange> SendEventVectorAsync(
        RegisteredPhysicalOwnerV1 destination, KeyLoad.Server.Features.EventStreams.EventVectorRemoteCall originalCall,
        KeyLoad.Server.Features.EventStreams.EventVectorRemoteEncoding encoding,
        KeyLoad.Core.ReadExecutionBudget work, CancellationToken originalCancellationToken)
        => KeyLoad.Server.Features.EventStreams.EventVectorRemoteExchange.SendAsync(this, options,
            membership, clock, destination, originalCall, encoding, work, originalCancellationToken);

    internal Task<KeyLoad.Core.Features.Messaging.RemoteQueueTransferPeerResult> TransferAsync(
        KeyLoad.Core.Features.Messaging.RemoteQueueTransferPeerCall call, CancellationToken token)
        => KeyLoad.Server.Features.Messaging.RemoteTransferPeerExchange.SendAsync(this, options, membership, clock, call, token);

    private async Task<RemoteDocumentReplyV1> ReadReplyAsync(RemoteDocumentCallV1 call,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var destination = call.Fence.Destination;
        var body = RemoteDocumentWire.Encode(new RemoteDocumentTransportEnvelope(call, null));
        using var sourceMac = RemoteDocumentMac.FromConfiguredSecret(options.PeerSecret);
        var original = await SendNativeAsync(destination, call.Source, RemoteModelNativeChannel.DocumentEnvelope,
            body, sourceMac.Sign(body, reply: false), call.ExpiresAt, RemoteDocumentWire.ReplyMaximum(call),
            cancellationToken).ConfigureAwait(false);
        using var targetMac = RemoteDocumentMac.FromConfiguredSecret(options.PhysicalGroupTrust.ForeignPeerSecret);
        if (!targetMac.Verify(original.Payload.Span, original.Signature, reply: true))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof);
        }
        var reply = NativeSerialization.Deserialize<RemoteDocumentReplyV1>(original.Payload.Span);
        Verify(call, reply, cancellationToken);
        await RequireCurrentReplyAsync(destination, original, reply.EndpointDiscovery.SiloAddress,
            cancellationToken).ConfigureAwait(false);
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, reply.SafeDetail ?? RemoteDocumentProtocol.Unavailable);
        }
        return reply;
    }

    private void Verify(RemoteDocumentCallV1 call, RemoteDocumentReplyV1 reply,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = call.Fence.Destination.Owner;
        if (call.ExpiresAt <= clock.GetUtcNow() || reply.RequestId != call.RequestId || reply.Nonce != call.Nonce
            || reply.EndpointDiscovery is not { TransportReady: true } discovery
            || discovery.VoterId != owner.VoterIds[SelectedVoter] || discovery.Incarnation != owner.Incarnation
            || discovery.ClusterId != options.ClusterId
            || discovery.ApplicationRpcVersion != GrainRoutingProtocol.RequestInterfaceVersion
            || discovery.PeerEnvelopeVersion != ReplicaTransportProtocol.Version
            || discovery.RuntimeJournalReaderContract != StoreReaderContract.RuntimeJournal
            || !PhysicalGroupAddress.CanonicalAddress(discovery.SiloAddress, membership)
            || reply.Controlled is not null || reply.ControlledBlob is not null || reply.QueueTransfer is not null
            || (reply.Error is null) != (reply.Result is not null || reply.QueryLeaf is not null || reply.SearchLeaf is not null)
            || reply.Result is not null && (call.Request is null || reply.QueryLeaf is not null || reply.SearchLeaf is not null)
            || reply.QueryLeaf is not null && (call.QueryLeaf is null || reply.SearchLeaf is not null)
            || reply.SearchLeaf is not null && (call.SearchLeaf is null || reply.Result is not null || reply.QueryLeaf is not null)
            || reply.Error is not null && (reply.Result is not null || reply.QueryLeaf is not null || reply.SearchLeaf is not null)
            || reply.Error is null && reply.SafeDetail is not null)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof);
        }
        if (reply.Result is { } result && (result.NodeId == Guid.Empty || result.Incarnation != owner.Incarnation
            || result.AppliedPosition < EmptyApplied || result.StorePosition < EmptyApplied
            || result.Placement.PhysicalShardId != owner.PhysicalShardId
            || result.Placement.Incarnation != owner.Incarnation || result.Placement.Partition != RemotePartitionQueryScope.Partition(call)
            || result.Placement.PlacementEpoch != owner.PlacementEpoch
            || !result.Placement.VoterIds.SequenceEqual(owner.VoterIds, StringComparer.Ordinal)))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable);
        }
        if (reply.QueryLeaf is { } leaf && (leaf.NodeId == Guid.Empty || leaf.Incarnation != owner.Incarnation
            || leaf.ReadGeneration < EmptyApplied || leaf.Partition != RemotePartitionQueryScope.Partition(call)))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable);
        }
        if (reply.SearchLeaf is { } search && (search.Witness is null || search.Witness.NodeId == Guid.Empty
            || search.Witness.Incarnation != owner.Incarnation || search.Witness.ReadGeneration < EmptyApplied
            || search.Witness.Partition != RemotePartitionQueryScope.Partition(call)))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable);
        }
        if (reply.Error is { } error && (!Enum.IsDefined(error) || string.IsNullOrWhiteSpace(reply.SafeDetail)))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof);
        }
    }

    private RemoteModelNativeClient Native
    {
        get
        {
            var existing = Volatile.Read(ref native);
            if (existing is not null)
            {
                return existing;
            }
            var borrowed = borrowNative();
            return Interlocked.CompareExchange(ref native, borrowed, null) ?? borrowed;
        }
    }

    public ValueTask DisposeAsync()
        => Volatile.Read(ref native)?.DisposeAsync() ?? ValueTask.CompletedTask;
}
