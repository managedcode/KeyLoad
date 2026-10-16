using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.QueryExecution;

internal sealed class RemoteDistributedSearchLeafCall(OrleansNode node, PartitionHost partition,
    IOptions<NodeOptions> nodeOptions, RemoteDocumentClient client, IResourceLifetimeWorkBinding binding)
{
    private const string NonceFormat = "N";

    internal RemoteDocumentCallV1? Prepare(GrainRequestEnvelope envelope,
        DistributedSearchOwnedLeafV1 request, RemoteDocumentReadFenceV1 fence)
    {
        var local = PhysicalOwnerConfiguredTuples.Local(nodeOptions.Value, partition);
        if (fence.Destination.Owner.PhysicalShardId == local.Owner.PhysicalShardId)
        {
            return null;
        }
        if (!PhysicalOwnerEntryValidation.Same(fence.Destination,
            PhysicalOwnerConfiguredTuples.Destination(nodeOptions.Value, partition)))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDistributedSearchSourceFence.Changed);
        }
        var discovery = node.Discovery?.Read()
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDistributedSearchSourceFence.Changed);
        return new(RemoteDocumentProtocol.Version, Guid.NewGuid(), Guid.NewGuid().ToString(NonceFormat),
            envelope.ExpiresAt, partition.Configuration.LocalId, discovery.SiloAddress, local.Owner, fence,
            null, SearchLeaf: request);
    }

    internal async Task<DistributedSearchLeafResultV1> RunAsync(DateTimeOffset expiresAt, string principalId,
        DistributedSearchOwnedLeafV1 request, RemoteDocumentCallV1? call, PartitionQueryLifetimeMetadata metadata,
        CancellationToken token)
        => call is null
            ? await RemoteQueryLifetimeLocalRead.SearchAsync(node, partition, principalId, request, metadata,
                expiresAt, token, binding).ConfigureAwait(false)
            : await RemoteQueryLifetimeExchange.ReadAsync<DistributedSearchLeafResultV1>(client, call, metadata,
                PartitionQueryParallelRetention.ReplyBytes(request.MaxResultBytes), token).ConfigureAwait(false);

    internal static long Measure(DistributedSearchOwnedLeafV1 request, RemoteDocumentCallV1? call,
        PartitionQueryLifetimeMetadata metadata)
        => call is null ? NativeSerialization.Measure(new DistributedSearchOwnedLeafV2(request,
            metadata.Definition, metadata.Coverage, metadata.Principal)) : RemoteQueryLifetimeExchange.Measure(call, metadata,
                PartitionQueryParallelRetention.ReplyBytes(request.MaxResultBytes));

}
