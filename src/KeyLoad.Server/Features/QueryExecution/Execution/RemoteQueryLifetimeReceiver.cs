using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.QueryExecution;

internal sealed class RemoteQueryLifetimeReceiver(OrleansNode node, PartitionHost partition,
    ServerRuntimeOptions options, TimeProvider clock)
{
    private RemoteDocumentReceiver Receiver => new(node, partition, options.Node,
        options.GrainRouting, options.Membership, clock);

    internal async Task<ReadOnlyMemory<byte>> ExecuteAsync(RemoteResourceLifetimeReadCallV2 call,
        CancellationToken token, IResourceLifetimeWorkBinding binding)
    {
        var request = NativeSerialization.Deserialize<RemoteQueryLifetimeRequestV2>(call.Payload.Span);
        RemoteQueryLifetimeScope.Require(call, request, Receiver, partition.Database.Limits, token);
        var original = request.Original;
        var principal = partition.Database.Store.Read(view => partition.Database.Principal(view,
            original.Fence.PrincipalId, clock.GetUtcNow()));
        if (principal.TenantId != original.Fence.Tenant)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, RemoteDocumentProtocol.InvalidProof);
        }
        var metadata = new PartitionQueryLifetimeMetadata(request.Definition, request.Coverage, request.Principal);
        object value = original.QueryLeaf is { } query
            ? await RemoteQueryLifetimeLocalRead.QueryAsync(node, partition, principal.Id, query,
                metadata, call.ExpiresAt, token, binding).ConfigureAwait(false)
            : await RemoteQueryLifetimeLocalRead.SearchAsync(node, partition, principal.Id, original.SearchLeaf!,
                metadata, call.ExpiresAt, token, binding).ConfigureAwait(false);
        RemoteQueryLifetimeScope.Require(call, request, Receiver, partition.Database.Limits, token);
        RequireResult(original, value);
        return original.QueryLeaf is not null
            ? EncodeResult((PartitionQueryLeafResultV1)value, call.MaximumReplyBytes)
            : EncodeResult((DistributedSearchLeafResultV1)value, call.MaximumReplyBytes);
    }

    private static byte[] EncodeResult<T>(T value, int maximumBytes)
    {
        if (NativeSerialization.Measure(value) > maximumBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, RemoteDocumentProtocol.InvalidProof);
        }
        return NativeSerialization.Serialize(value);
    }

    private void RequireResult(RemoteDocumentCallV1 original, object? value)
    {
        var actual = partition.Database.Store.Identity;
        var valid = original.QueryLeaf is not null
            ? value is PartitionQueryLeafResultV1 query && query.NodeId == actual.NodeId
                && query.Incarnation == actual.Incarnation && query.ReadGeneration == actual.ReadGeneration
            : value is DistributedSearchLeafResultV1 search && search.Witness is { } witness
                && witness.NodeId == actual.NodeId && witness.Incarnation == actual.Incarnation
                && witness.ReadGeneration == actual.ReadGeneration;
        if (!valid)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable);
        }
    }
}
