using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.QueryExecution;

internal sealed class RemoteDistributedSearchLeafCall(OrleansNode node, PartitionHost partition,
    IOptions<NodeOptions> nodeOptions, RemoteDocumentClient client)
{
    private const string NonceFormat = "N";

    internal RemoteDocumentCallV1? Prepare(GrainRequestEnvelope envelope,
        DistributedSearchOwnedLeafV1 request, RemoteDocumentReadFenceV1 fence)
    {
        var local = PhysicalOwnerConfiguredTuples.Local(nodeOptions.Value, partition);
        if (fence.Destination.Owner.PhysicalShardId == local.Owner.PhysicalShardId)
        { return null; }
        if (!PhysicalOwnerEntryValidation.Same(fence.Destination,
            PhysicalOwnerConfiguredTuples.Destination(nodeOptions.Value, partition)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDistributedSearchSourceFence.Changed); }
        var discovery = node.Discovery?.Read()
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDistributedSearchSourceFence.Changed);
        return new(RemoteDocumentProtocol.Version, Guid.NewGuid(), Guid.NewGuid().ToString(NonceFormat),
            envelope.ExpiresAt, partition.Configuration.LocalId, discovery.SiloAddress, local.Owner, fence,
            null, SearchLeaf: request);
    }

    internal async Task<DistributedSearchLeafResultV1> RunAsync(DateTimeOffset expiresAt, string principalId,
        DistributedSearchOwnedLeafV1 request, RemoteDocumentCallV1? call, CancellationToken token)
        => call is null
            ? await ReadLocalAsync(request, expiresAt, principalId, token).ConfigureAwait(false)
            : await client.ReadSearchLeafAsync(call, token).ConfigureAwait(false);

    private async Task<DistributedSearchLeafResultV1> ReadLocalAsync(DistributedSearchOwnedLeafV1 request,
        DateTimeOffset expiresAt, string principalId, CancellationToken token)
    {
        var principal = partition.Database.Store.Read(view => partition.Database.Principal(view,
            principalId, partition.Database.EvaluationClock.GetUtcNow()));
        var requestId = Guid.NewGuid();
        var signed = node.CatalogRequestCodec().CreateDistributedSearchLeaf(requestId, principal.Id,
            NativeSerialization.Serialize(request), expiresAt);
        using var identity = node.OpenRequestContext(principal, requestId, Guid.Empty, token);
        var reply = await node.ExecuteAsync(requestId, signed, command: false, cancellationToken: token)
            .ConfigureAwait(false);
        return GrainNativePayload.Read<GrainValue>(reply.Payload).Value as DistributedSearchLeafResultV1
            ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
    }
}
