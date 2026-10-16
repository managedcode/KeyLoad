using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Query;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.QueryExecution;

internal sealed class RemotePartitionQueryLeafRead(OrleansNode node, PartitionHost partition,
    IOptions<NodeOptions> nodeOptions, RemoteDocumentClient client)
{
    private NodeOptions Options => nodeOptions.Value;
    private const int EmptyGrant = 0;
    private const string NonceFormat = "N";
    private const int MetadataPassesRemaining = 2;
    private const string FenceChanged = "The remote partition query authority changed.";
    private readonly List<(PartitionQueryLeafPlanV1 Plan, PartitionQuerySourceFenceV2 Fence,
        ReadExecutionBudgetReadGrant Grant, ReadExecutionBudget Budget)> original = [];

    internal PartitionQueryPreparedLeaf Prepare(GrainRequestEnvelope envelope, string principalId,
        PartitionQueryLeafPlanV1 plan, ReadExecutionBudgetReadGrant grant, ReadExecutionBudget budget)
    {
        budget.Check();
        var beforeBytes = grant.ReadBytes;
        var beforeRecords = grant.ExaminedRecords;
        var captured = Capture(principalId, plan, grant, budget);
        var fence = captured.Authority;
        original.Add((plan, captured, grant, budget));
        var metadataBytes = grant.ReadBytes - beforeBytes;
        var metadataRecords = grant.ExaminedRecords - beforeRecords;
        var child = plan with
        {
            MaxReadBytes = grant.RemainingBytes - checked(metadataBytes * MetadataPassesRemaining),
            MaxExaminedRecords = grant.RemainingRecords - checked(metadataRecords * MetadataPassesRemaining)
        };
        if (child.MaxReadBytes < EmptyGrant || child.MaxExaminedRecords < EmptyGrant)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, FenceChanged);
        }
        var local = PhysicalOwnerConfiguredTuples.Local(Options, partition);
        var request = new PartitionQueryOwnedLeafRequest(child, fence.Destination.Owner, fence.Tenant);
        var call = PrepareCall(envelope, request, fence, local);
        var wireBytes = Measure(request, call, captured.Metadata);
        child = PartitionQueryParallelRetention.Child(child, wireBytes);
        request = request with
        {
            Plan = child
        };
        call = call is null ? null : call with
        {
            QueryLeaf = request
        };
        if (Measure(request, call, captured.Metadata) > wireBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, FenceChanged);
        }
        return new(actualToken => RunAsync(envelope.ExpiresAt, principalId, request, call, captured.Metadata, actualToken),
            (actual, actualToken) =>
            {
                actualToken.ThrowIfCancellationRequested();
                budget.Check();
                PartitionQueryMerge.ValidateLeaf(child, actual.Result, plan.Partition,
                    partition.Database.Store.Identity, budget, fence.Destination.Owner);
                budget.ImportReadGrant(grant, actual.Result.ReadBytes, actual.Result.ExaminedRecords);
                RequireSame(captured, Capture(principalId, plan, grant, budget));
                return Task.CompletedTask;
            });
    }

    private RemoteDocumentCallV1? PrepareCall(GrainRequestEnvelope envelope,
        PartitionQueryOwnedLeafRequest request, RemoteDocumentReadFenceV1 fence, RegisteredPhysicalOwnerV1 local)
    {
        if (fence.Destination.Owner.PhysicalShardId == local.Owner.PhysicalShardId)
        {
            return null;
        }
        if (!KeyLoad.Core.Features.ClusterRouting.Validation.PhysicalOwnerEntryValidation.Same(fence.Destination,
            PhysicalOwnerConfiguredTuples.Destination(Options, partition)))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, FenceChanged);
        }
        var discovery = node.Discovery?.Read() ?? throw Errors.Fail(ErrorCode.OwnershipLost, FenceChanged);
        return new(RemoteDocumentProtocol.Version, Guid.NewGuid(), Guid.NewGuid().ToString(NonceFormat),
            envelope.ExpiresAt, partition.Configuration.LocalId, discovery.SiloAddress, local.Owner, fence, null, request);
    }

    private async Task<PartitionQueryOwnedLeaf> RunAsync(DateTimeOffset expiresAt, string principalId,
        PartitionQueryOwnedLeafRequest request, RemoteDocumentCallV1? call, PartitionQueryLifetimeMetadata metadata,
        CancellationToken token)
    {
        var result = call is null
            ? await RemoteQueryLifetimeLocalRead.QueryAsync(node, partition, principalId, request, metadata,
                expiresAt, token).ConfigureAwait(false)
            : await RemoteQueryLifetimeExchange.ReadAsync<PartitionQueryLeafResultV1>(client, call, metadata,
                PartitionQueryParallelRetention.ReplyBytes(request.Plan), token).ConfigureAwait(false);
        return new(request.Owner, result);
    }

    internal async Task RevalidateAsync(string principalId, CancellationToken token)
    {
        await partition.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
        foreach (var item in original)
        {
            token.ThrowIfCancellationRequested();
            RequireSame(item.Fence, Capture(principalId, item.Plan, item.Grant, item.Budget));
        }
    }

    private PartitionQuerySourceFenceV2 Capture(string principalId, PartitionQueryLeafPlanV1 plan,
        ReadExecutionBudgetReadGrant grant, ReadExecutionBudget budget)
        => partition.Database.WithPartitionQueryFenceView(principalId, plan.Partition, plan.Request.Query.Collection, grant,
            (view, principal, resource, digest) =>
            {
                QueryFieldAuthorization.RequireSchema(partition.Database, principal, resource, plan.Request);
                return RemoteQueryLifetimeSource.Capture(partition.Database, view, principal, resource, digest,
                    plan.Partition, grant, budget);
            });

    private static void RequireSame(PartitionQuerySourceFenceV2 expected, PartitionQuerySourceFenceV2 actual)
        => RemoteQueryLifetimeSource.RequireSame(expected, actual, FenceChanged);

    private static long Measure(PartitionQueryOwnedLeafRequest request, RemoteDocumentCallV1? call,
        PartitionQueryLifetimeMetadata metadata)
        => call is null ? NativeSerialization.Measure(new PartitionQueryOwnedLeafRequestV2(request,
            metadata.Definition, metadata.Coverage, metadata.Principal)) : RemoteQueryLifetimeExchange.Measure(call, metadata,
                PartitionQueryParallelRetention.ReplyBytes(request.Plan));

}
