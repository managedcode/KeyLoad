using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.BlobStorage;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.Messaging;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class RemoteDocumentEndpoint : IAsyncDisposable
{
    private const int AbsentEnvelopeCount = 0;
    private const int PresentEnvelopeCount = 1;
    private const int RequiredEnvelopeCount = 1;
    private readonly NodeOptions options;
    private readonly RemoteDocumentReceiver receiver;
    private readonly RemoteControlledDocumentReceiver controlled;
    private readonly RemoteControlledBlobReceiver controlledBlob;
    private readonly RemoteTransferPeerReceiver? queueTransfer;
    private readonly RemoteDocumentWorkOwner owner;
    private readonly PartitionMovementReplayCache replay;
    private readonly GrainRoutingOptions routing;
    private readonly TimeProvider clock;

    internal RemoteDocumentEndpoint(IOptions<NodeOptions> nodeOptions, RemoteDocumentReceiver receiver,
        RemoteDocumentWorkOwner owner, RemoteControlledDocumentReceiver controlled,
        RemoteControlledBlobReceiver controlledBlob, IOptions<OrleansMembershipOptions> membership,
        IOptions<GrainRoutingOptions> routing, TimeProvider clock, RemoteTransferPeerReceiver? queueTransfer = null)
    {
        options = nodeOptions.Value;
        this.receiver = receiver;
        this.owner = owner;
        this.controlled = controlled;
        this.controlledBlob = controlledBlob;
        this.queueTransfer = queueTransfer;
        this.routing = routing.Value;
        this.clock = clock;
        replay = new(clock, membership);
    }

    internal Task<(ReadOnlyMemory<byte> Payload, string Signature)> ExecuteAsync(
        RemoteModelNativeRequest request, CancellationToken caller)
        => ExecuteAsync(request, null, caller);

    internal async Task<(ReadOnlyMemory<byte> Payload, string Signature)> ExecuteAsync(
        RemoteModelNativeRequest request, RemoteModelNativeEffectBoundary? effectBoundary, CancellationToken caller)
    {
        if (!options.PhysicalGroupTrust.RemoteDocumentReads
            || options.PhysicalGroupTrust.Role != PhysicalGroupTrustRole.Destination)
        { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable); }
        var failures = new List<Exception>();
        RemoteDocumentWorkOwner.Operation? operation = null;
        (ReadOnlyMemory<byte> Payload, string Signature)? result = null;
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                operation = owner.Acquire(Guid.NewGuid());
                result = await ExecuteOwnedAsync(request, operation, effectBoundary, caller).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }
        finally
        {
            if (operation is not null)
            { ServerFailureObserver.Observe(operation.Dispose, failures); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
    }

    private async Task<(ReadOnlyMemory<byte> Payload, string Signature)> ExecuteOwnedAsync(
        RemoteModelNativeRequest request, RemoteDocumentWorkOwner.Operation operation,
        RemoteModelNativeEffectBoundary? effectBoundary, CancellationToken caller)
    {
        using var deadline = new CancellationTokenSource(routing.ExecutionLifetime, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, operation.ShutdownToken, deadline.Token);
        var token = linked.Token;
        using var mac = RemoteDocumentMac.FromConfiguredSecret(options.PhysicalGroupTrust.ForeignPeerSecret);
        if (!mac.Verify(request.Payload.Span, request.Signature, reply: false))
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof); }
        var transport = RemoteDocumentWire.DecodeCall(request.Payload.Span);
        RequireEnvelope(request, transport);
        if (transport.QueueTransfer is { } transfer)
        {
            var actual = queueTransfer ?? throw Errors.Fail(ErrorCode.UnsupportedCapability, Core.Features.Messaging.RemoteTransferPeerProtocol.Unavailable);
            return await RemoteTransferPeerEndpointExecution.ExecuteAsync(transfer, request.Payload, request.Signature,
                actual, options, replay, clock, effectBoundary, token).ConfigureAwait(false);
        }
        if (transport.ControlledBlob is { } blob)
        { return await RemoteControlledBlobEndpointExecution.ExecuteAsync(blob, controlledBlob, options, replay, clock, effectBoundary, token).ConfigureAwait(false); }
        if (transport.Controlled is { } document)
        { return await RemoteControlledDocumentEndpointExecution.ExecuteAsync(document, controlled, options, replay, clock, effectBoundary, token).ConfigureAwait(false); }
        return await ExecuteDocumentCallAsync(transport.Document!, token).ConfigureAwait(false);
    }

    private static void RequireEnvelope(RemoteModelNativeRequest request, RemoteDocumentTransportEnvelope transport)
    {
        var count = (transport.Document is null ? AbsentEnvelopeCount : PresentEnvelopeCount) + (transport.Controlled is null ? AbsentEnvelopeCount : PresentEnvelopeCount)
            + (transport.ControlledBlob is null ? AbsentEnvelopeCount : PresentEnvelopeCount) + (transport.QueueTransfer is null ? AbsentEnvelopeCount : PresentEnvelopeCount);
        if (count != RequiredEnvelopeCount)
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof); }
        var source = transport.Document?.Source ?? transport.Controlled?.Source ?? transport.ControlledBlob?.Source
            ?? transport.QueueTransfer!.SourceOwner.Owner;
        var destination = transport.Document?.Fence.Destination.Owner ?? transport.Controlled?.Destination.Owner
            ?? transport.ControlledBlob?.Destination.Owner ?? transport.QueueTransfer!.DestinationOwner.Owner;
        var expiry = transport.Document?.ExpiresAt ?? transport.Controlled?.Request.Frame.ExpiresAt
            ?? transport.ControlledBlob?.Request.Frame.ExpiresAt ?? transport.QueueTransfer!.ExpiresAt;
        if (!PhysicalOwnerEntryValidation.SameOwner(source, request.SourceOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(destination, request.DestinationOwner) || expiry != request.ExpiresAt)
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof); }
    }

    private async Task<(ReadOnlyMemory<byte> Payload, string Signature)> ExecuteDocumentCallAsync(RemoteDocumentCallV1 call, CancellationToken token)
    {
        receiver.Validate(call, token);
        if (!replay.TryUse(call.Nonce))
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof); }
        var remaining = call.ExpiresAt - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteDocumentProtocol.Unavailable); }
        using var originalExpiry = new CancellationTokenSource(remaining, clock);
        using var original = CancellationTokenSource.CreateLinkedTokenSource(token, originalExpiry.Token);
        RemoteDocumentReplyV1 reply;
        try
        {
            if (call.SearchLeaf is not null)
            {
                var leaf = await receiver.ReadSearchLeafAsync(call, original.Token).ConfigureAwait(false);
                reply = new(call.RequestId, call.Nonce, null, null, null, receiver.Discovery(), SearchLeaf: leaf);
            }
            else if (call.QueryLeaf is not null)
            {
                var leaf = await receiver.ReadLeafAsync(call, original.Token).ConfigureAwait(false);
                reply = new(call.RequestId, call.Nonce, null, null, null, receiver.Discovery(), leaf);
            }
            else
            {
                var result = await receiver.ReadAsync(call, original.Token).ConfigureAwait(false);
                reply = new(call.RequestId, call.Nonce, result, null, null, receiver.Discovery());
            }
        }
        catch (KeyLoadException error) when (error.Code != ErrorCode.Corruption)
        { reply = new(call.RequestId, call.Nonce, null, error.Code, error.Message, receiver.Discovery()); }
        var encoded = RemoteDocumentWire.Encode(reply, RemoteDocumentWire.ReplyMaximum(call));
        using var replyMac = RemoteDocumentMac.FromConfiguredSecret(options.PeerSecret);
        original.Token.ThrowIfCancellationRequested();
        return (encoded, replyMac.Sign(encoded, reply: true));
    }

    public ValueTask DisposeAsync()
    {
        replay.Dispose();
        return ValueTask.CompletedTask;
    }
}
