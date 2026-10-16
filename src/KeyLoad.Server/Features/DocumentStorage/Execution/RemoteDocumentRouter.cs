using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class RemoteDocumentRouter(OrleansNode node, PartitionHost partition,
    IOptions<NodeOptions> nodeOptions, IOptions<DatabaseLimits> limits, RemoteDocumentClient client,
    RemoteDocumentWorkOwner owner, TimeProvider clock) : IRemoteDocumentReadRouter, IRemoteQueueTransferReceiptRouter
{

    public async Task<DocumentResult?> ReadAsync(GrainRequestEnvelope envelope, PrincipalRecord principal,
        GetDocumentRequest request, CancellationToken cancellationToken, IResourceLifetimeWorkBinding binding)
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
                var work = new ReadExecutionBudget(limits, clock, original.Token);
                var controlled = await new ControlledDocumentSourceRead(node, partition, nodeOptions, limits, client, clock)
                    .TryReadAsync(envelope, principal.Id, request, work, original.Token, binding).ConfigureAwait(false);
                if (controlled.Routed)
                {
                    document = controlled.Document;
                    return;
                }
                var ordinary = await new OrdinaryDocumentLifetimeSourceRead(node, partition, nodeOptions, limits, client, clock)
                    .TryReadAsync(envelope, principal.Id, request, work, original.Token, binding).ConfigureAwait(false);
                document = ordinary.Routed ? ordinary.Document
                    : partition.Database.GetDocumentBound(principal.Id, request, work, binding);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return document;
    }

    public Task<RemoteQueueTransferReceiptRead> InspectTransferAsync(GrainRequestEnvelope envelope,
        PrincipalRecord principal, InspectQueueTransferReceiptRequest request, CancellationToken cancellationToken, IResourceLifetimeWorkBinding binding)
        => new KeyLoad.Server.Features.Messaging.RemoteTransferReceiptRouter(node, partition, nodeOptions,
            limits, client, owner, clock).ReadAsync(envelope, principal, request, cancellationToken, binding);

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
