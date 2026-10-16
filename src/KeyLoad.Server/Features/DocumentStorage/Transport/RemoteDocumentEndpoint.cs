using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Validation;
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
    private readonly RemoteControlledBlobReceiver controlledBlob;
    private readonly RemoteTransferPeerReceiver? queueTransfer;
    private readonly RemoteDocumentWorkOwner owner;
    private readonly PartitionMovementReplayCache replay;
    private readonly GrainRoutingOptions routing;
    private readonly TimeProvider clock;
    private readonly RemoteResourceLifetimeReadEndpoint? lifetime;

    internal RemoteDocumentEndpoint(IOptions<NodeOptions> nodeOptions, RemoteDocumentWorkOwner owner,
        RemoteControlledBlobReceiver controlledBlob, IOptions<OrleansMembershipOptions> membership,
        IOptions<GrainRoutingOptions> routing, TimeProvider clock, RemoteTransferPeerReceiver? queueTransfer = null,
        RemoteResourceLifetimeReadEndpoint? lifetime = null)
    {
        options = nodeOptions.Value;
        this.owner = owner;
        this.controlledBlob = controlledBlob;
        this.queueTransfer = queueTransfer;
        this.lifetime = lifetime;
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
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable);
        }
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
            {
                ServerFailureObserver.Observe(operation.Dispose, failures);
            }
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
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof);
        }
        if (request.Channel == RemoteModelNativeChannel.ResourceLifetimeRead)
        {
            return await (lifetime ?? throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteResourceLifetimeReadProtocol.Unavailable))
                .ExecuteAsync(request, replay, token).ConfigureAwait(false);
        }
        if (request.Channel == RemoteModelNativeChannel.ResourceLifetimeBlobOutcome)
        {
            var outcome = NativeSerialization.Deserialize<RemoteControlledBlobOutcomeCallV2>(request.Payload.Span);
            RemoteControlledBlobOutcomeEndpointV2.RequireEnvelope(request, outcome);
            return await RemoteControlledBlobOutcomeEndpointV2.ExecuteAsync(outcome, controlledBlob,
                options, replay, clock, effectBoundary, token).ConfigureAwait(false);
        }
        var transport = RemoteDocumentWire.DecodeCall(request.Payload.Span);
        RequireEnvelope(request, transport);
        if (transport.QueueTransfer is { } transfer)
        {
            var actual = queueTransfer ?? throw Errors.Fail(ErrorCode.UnsupportedCapability, Core.Features.Messaging.RemoteTransferPeerProtocol.Unavailable);
            return await RemoteTransferPeerEndpointExecution.ExecuteAsync(transfer, request.Payload, request.Signature,
                actual, options, replay, clock, effectBoundary, token).ConfigureAwait(false);
        }
        // Native channel zero retains queue coordination only. Blob observations require their V2 channel.
        throw Errors.Fail(ErrorCode.Unauthenticated, RemoteResourceLifetimeReadProtocol.InvalidProof);
    }

    private static void RequireEnvelope(RemoteModelNativeRequest request, RemoteDocumentTransportEnvelope transport)
    {
        var count = (transport.Document is null ? AbsentEnvelopeCount : PresentEnvelopeCount) + (transport.Controlled is null ? AbsentEnvelopeCount : PresentEnvelopeCount)
            + (transport.ControlledBlob is null ? AbsentEnvelopeCount : PresentEnvelopeCount) + (transport.QueueTransfer is null ? AbsentEnvelopeCount : PresentEnvelopeCount);
        if (count != RequiredEnvelopeCount)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof);
        }
        var source = transport.Document?.Source ?? transport.Controlled?.Source ?? transport.ControlledBlob?.Source
            ?? transport.QueueTransfer!.SourceOwner.Owner;
        var destination = transport.Document?.Fence.Destination.Owner ?? transport.Controlled?.Destination.Owner
            ?? transport.ControlledBlob?.Destination.Owner ?? transport.QueueTransfer!.DestinationOwner.Owner;
        var expiry = transport.Document?.ExpiresAt ?? transport.Controlled?.Request.Frame.ExpiresAt
            ?? transport.ControlledBlob?.Request.Frame.ExpiresAt ?? transport.QueueTransfer!.ExpiresAt;
        if (!PhysicalOwnerEntryValidation.SameOwner(source, request.SourceOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(destination, request.DestinationOwner) || expiry != request.ExpiresAt)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof);
        }
    }

    public ValueTask DisposeAsync()
    {
        replay.Dispose();
        return ValueTask.CompletedTask;
    }
}
