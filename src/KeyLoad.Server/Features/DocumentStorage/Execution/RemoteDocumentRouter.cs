using KeyLoad.Clustering;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class RemoteDocumentRouter(OrleansNode node, PartitionHost partition,
    IOptions<NodeOptions> nodeOptions, IOptions<DatabaseLimits> limits, RemoteDocumentClient client,
    RemoteDocumentWorkOwner owner, TimeProvider clock) : IRemoteDocumentReadRouter, IRemoteQueueTransferReceiptRouter
{

    public async Task<DocumentResult?> ReadAsync(GrainRequestEnvelope envelope, PrincipalRecord principal,
        GetDocumentRequest request, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        DocumentResult? document = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var operation = owner.Acquire(envelope.RequestId);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var originalExpiry = OriginalExpiry(envelope.ExpiresAt);
                using var original = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
                    operation.ShutdownToken, originalExpiry.Token);
                var controlled = await new ControlledDocumentSourceRead(node, partition, nodeOptions, limits, client, clock)
                    .TryReadAsync(envelope, principal.Id, request, original.Token).ConfigureAwait(false);
                if (controlled.Routed)
                {
                    document = controlled.Document;
                    return;
                }
                var ordinary = await new OrdinaryDocumentLifetimeSourceRead(node, partition, nodeOptions, limits, client, clock)
                    .TryReadAsync(envelope, principal.Id, request, original.Token).ConfigureAwait(false);
                document = ordinary.Routed ? ordinary.Document
                    : partition.Database.GetDocument(principal.Id, request.Reference, request.MinimumToken, original.Token);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return document;
    }

    public Task<RemoteQueueTransferReceiptRead> InspectTransferAsync(GrainRequestEnvelope envelope,
        PrincipalRecord principal, InspectQueueTransferReceiptRequest request, CancellationToken cancellationToken)
        => new KeyLoad.Server.Features.Messaging.RemoteTransferReceiptRouter(node, partition, nodeOptions,
            limits, client, owner, clock).ReadAsync(envelope, principal, request, cancellationToken);

    private CancellationTokenSource OriginalExpiry(DateTimeOffset expiresAt)
    {
        var remaining = expiresAt - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteDocumentProtocol.InvalidProof);
        }
        return new CancellationTokenSource(remaining, clock);
    }
}
