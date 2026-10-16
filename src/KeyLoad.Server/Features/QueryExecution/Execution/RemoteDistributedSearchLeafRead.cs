using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.QueryExecution;

internal sealed class RemoteDistributedSearchLeafRead(OrleansNode node, PartitionHost partition,
    IOptions<NodeOptions> nodeOptions, RemoteDocumentClient client)
{
    private const int EmptyGrant = 0;
    private const int CurrentVersion = 1;
    private const int MetadataPassesRemaining = 2;
    private readonly Dictionary<PartitionRef, PartitionQuerySourceFenceV2> original = [];
    private readonly RemoteDistributedSearchLeafCall calls = new(node, partition, nodeOptions, client);

    internal DistributedSearchPreparedLeaf Prepare(GrainRequestEnvelope envelope, string principalId,
        DistributedSearchPhaseWork work, ReadExecutionBudgetReadGrant grant, ReadExecutionBudget budget)
    {
        budget.Check();
        var beforeBytes = grant.ReadBytes;
        var beforeRecords = grant.ExaminedRecords;
        var captured = Capture(principalId, work.Search, grant, budget);
        if (original.TryGetValue(work.Search.Partition, out var initial))
        {
            RemoteQueryLifetimeSource.RequireSame(initial, captured, RemoteDistributedSearchSourceFence.Changed);
        }
        else
        {
            budget.ChargeBytes(PartitionQueryRetention.LeafDescriptorBytes);
            original.Add(work.Search.Partition, captured);
        }
        var remainingBytes = grant.RemainingBytes
            - checked((grant.ReadBytes - beforeBytes) * MetadataPassesRemaining);
        var remainingRecords = grant.RemainingRecords
            - checked((grant.ExaminedRecords - beforeRecords) * MetadataPassesRemaining);
        if (remainingBytes < EmptyGrant || remainingRecords < EmptyGrant)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, RemoteDistributedSearchSourceFence.Changed);
        }
        var request = new DistributedSearchOwnedLeafV1(CurrentVersion, work.Phase, work.Search,
            captured.Authority.Destination.Owner, captured.Authority.Tenant, remainingBytes, remainingRecords,
            work.MaxResultBytes, work.Witness, work.Statistics, work.Scope, work.SourceWindowId, work.Selected);
        var call = calls.Prepare(envelope, request, captured.Authority);
        var wireBytes = RemoteDistributedSearchLeafCall.Measure(request, call, captured.Metadata);
        var childBytes = PartitionQueryParallelRetention.ChildBytes(work.MaxResultBytes, wireBytes,
            NativeSerialization.Measure(new DistributedSearchOwnedLeafV2(request, captured.Metadata.Definition, captured.Metadata.Coverage, captured.Metadata.Principal)));
        request = request with
        {
            MaxResultBytes = checked((int)childBytes)
        };
        call = call is null ? null : call with
        {
            SearchLeaf = request
        };
        if (RemoteDistributedSearchLeafCall.Measure(request, call, captured.Metadata) > wireBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, RemoteDistributedSearchSourceFence.Changed);
        }
        return new(request,
            token => calls.RunAsync(envelope.ExpiresAt, principalId, request, call, captured.Metadata, token),
            (result, token) =>
            {
                token.ThrowIfCancellationRequested();
                DistributedSearchLeafResultValidation.Require(request, result, budget);
                budget.ImportReadGrant(grant, result.ReadBytes, result.ExaminedRecords);
                RemoteQueryLifetimeSource.RequireSame(captured, Capture(principalId, work.Search, grant, budget),
                    RemoteDistributedSearchSourceFence.Changed);
                return Task.CompletedTask;
            });
    }

    internal Task RevalidateAsync(CancellationToken token)
        => partition.Coordinator.ReadBarrierAsync(token);

    private PartitionQuerySourceFenceV2 Capture(string principalId, SearchRequest request,
        ReadExecutionBudgetReadGrant grant, ReadExecutionBudget budget)
        => partition.Database.WithPartitionQueryFenceView<PartitionQuerySourceFenceV2>(principalId, request.Partition, request.Collection, grant,
            (view, principal, resource, digest) =>
            {
                partition.Database.Authorization.RequireSchemaVisibility(principal, resource);
                return RemoteQueryLifetimeSource.Capture(partition.Database, view, principal, resource, digest,
                    request.Partition, grant, budget);
            });
}
