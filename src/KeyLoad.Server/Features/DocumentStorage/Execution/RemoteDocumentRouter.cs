using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class RemoteDocumentRouter(OrleansNode node, PartitionHost partition,
    IOptions<NodeOptions> nodeOptions, RemoteDocumentClient client,
    RemoteDocumentWorkOwner owner, TimeProvider clock) : IRemoteDocumentReadRouter
{
    private const string NonceFormat = "N";

    public async Task<DocumentResult?> ReadAsync(GrainRequestEnvelope envelope, PrincipalRecord principal,
        GetDocumentRequest request, CancellationToken cancellationToken)
    {
        var fence = partition.Database.CaptureRemoteDocumentRead(principal.Id, request.Reference, cancellationToken);
        if (fence is null)
        { return partition.Database.GetDocument(principal.Id, request.Reference, request.MinimumToken, cancellationToken); }
        var settings = nodeOptions.Value.MembershipAuthority;
        if (!settings.RemoteDocumentReads || settings.Mode != MembershipAuthoritySettingsProtocol.Authority
            || !PhysicalOwnerEntryValidation.Same(fence.Destination,
                PhysicalOwnerConfiguredTuples.Destination(nodeOptions.Value, partition)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable); }
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
                var discovery = node.Discovery?.Read()
                    ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable);
                var call = new RemoteDocumentCallV1(RemoteDocumentProtocol.Version, envelope.RequestId,
                    Guid.NewGuid().ToString(NonceFormat), envelope.ExpiresAt, partition.Configuration.LocalId,
                    discovery.SiloAddress, PhysicalOwnerConfiguredTuples.Local(nodeOptions.Value, partition).Owner, fence, request);
                var result = await client.ReadAsync(call, original.Token).ConfigureAwait(false);
                await partition.Coordinator.ReadBarrierAsync(original.Token).ConfigureAwait(false);
                partition.Database.ValidateRemoteDocumentRead(fence, request.Reference, original.Token);
                original.Token.ThrowIfCancellationRequested();
                document = result.Document;
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return document;
    }

    private CancellationTokenSource OriginalExpiry(DateTimeOffset expiresAt)
    {
        var remaining = expiresAt - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteDocumentProtocol.InvalidProof); }
        return new CancellationTokenSource(remaining, clock);
    }
}
