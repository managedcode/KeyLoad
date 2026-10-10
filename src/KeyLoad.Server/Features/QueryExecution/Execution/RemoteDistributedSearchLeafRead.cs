using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Orleans;
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
    private readonly Dictionary<PartitionRef, PartitionQuerySourceFence> original = [];
    private readonly RemoteDistributedSearchLeafCall calls = new(node, partition, nodeOptions, client);

    internal DistributedSearchPreparedLeaf Prepare(GrainRequestEnvelope envelope, string principalId,
        DistributedSearchPhaseWork work, ReadExecutionBudgetReadGrant grant, ReadExecutionBudget budget)
    {
        budget.Check();
        var beforeBytes = grant.ReadBytes;
        var beforeRecords = grant.ExaminedRecords;
        var captured = Capture(principalId, work.Search, grant);
        if (original.TryGetValue(work.Search.Partition, out var initial))
        { RemoteDistributedSearchSourceFence.RequireSame(initial, captured); }
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
        { throw Errors.Fail(ErrorCode.BudgetExceeded, RemoteDistributedSearchSourceFence.Changed); }
        var request = new DistributedSearchOwnedLeafV1(CurrentVersion, work.Phase, work.Search,
            captured.Authority.Destination.Owner, captured.Authority.Tenant, remainingBytes, remainingRecords,
            work.MaxResultBytes, work.Witness, work.Statistics, work.Scope, work.SourceWindowId, work.Selected);
        var call = calls.Prepare(envelope, request, captured.Authority);
        var wireBytes = call is null ? NativeSerialization.Measure(request) : NativeSerialization.Measure(call);
        var childBytes = PartitionQueryParallelRetention.ChildBytes(work.MaxResultBytes, wireBytes,
            NativeSerialization.Measure(request));
        request = request with { MaxResultBytes = checked((int)childBytes) };
        call = call is null ? null : call with { SearchLeaf = request };
        if ((call is null ? NativeSerialization.Measure(request) : NativeSerialization.Measure(call)) > wireBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, RemoteDistributedSearchSourceFence.Changed); }
        return new(request,
            token => calls.RunAsync(envelope.ExpiresAt, principalId, request, call, token),
            (result, token) =>
            {
                token.ThrowIfCancellationRequested();
                DistributedSearchLeafResultValidation.Require(request, result, budget);
                budget.ImportReadGrant(grant, result.ReadBytes, result.ExaminedRecords);
                RemoteDistributedSearchSourceFence.RequireSame(captured, Capture(principalId, work.Search, grant));
                return Task.CompletedTask;
            });
    }

    internal Task RevalidateAsync(CancellationToken token)
        => partition.Coordinator.ReadBarrierAsync(token);

    private PartitionQuerySourceFence Capture(string principalId, SearchRequest request,
        ReadExecutionBudgetReadGrant grant)
        => partition.Database.WithPartitionQueryFenceView<PartitionQuerySourceFence>(principalId, request.Partition, request.Collection, grant,
            (view, principal, resource, digest) =>
            {
                DistributedSearchFieldAuthorization.Require(partition.Database, principal, resource, request);
                return new(partition.Database.CaptureRemotePartitionQuery(view, principal, request.Partition,
                    request.Collection, grant), digest);
            });
}
