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
        GetDocumentRequest request, IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
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
                var ordinary = await new OrdinaryDocumentLifetimeSourceRead(node, partition, nodeOptions, limits, client, clock)
                    .TryReadAsync(envelope, principal.Id, request, work, binding, original.Token).ConfigureAwait(false);
                document = ordinary.Routed ? ordinary.Document
                    : partition.Database.GetDocumentBound(principal.Id, request, work, binding);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return document;
    }

    public Task<RemoteQueueTransferReceiptRead> InspectTransferAsync(GrainRequestEnvelope envelope,
        PrincipalRecord principal, InspectQueueTransferReceiptRequest request, IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
        => new KeyLoad.Server.Features.Messaging.RemoteTransferReceiptRouter(node, partition, nodeOptions,
            limits, client, owner, clock).ReadAsync(envelope, principal, request, binding, cancellationToken);

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
