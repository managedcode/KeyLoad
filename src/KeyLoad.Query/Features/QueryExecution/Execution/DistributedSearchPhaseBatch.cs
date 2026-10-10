using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class DistributedSearchPhaseBatch
{
    private const int FirstLeaf = 0;

    internal static async Task<ImmutableArray<DistributedSearchLeafResultV1>> RunAsync(
        ImmutableArray<DistributedSearchPhaseWork> work, ReadExecutionBudgetReadGrant[] grants,
        int grantOffset, IOptions<QueryExecutionOptions> options, ReadExecutionBudget budget,
        Func<DistributedSearchPhaseWork, ReadExecutionBudgetReadGrant, ReadExecutionBudget, DistributedSearchPreparedLeaf> prepare,
        CancellationTokenSource execution)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(prepare);
        budget.Check();
        var policy = options.Value;
        policy.Validate();
        if (work.IsDefaultOrEmpty || work.Length > policy.MaximumPartitions)
        { throw Errors.Fail(ErrorCode.Corruption, DistributedSearchPhaseErrors.InvalidBatch); }
        budget.ChargeBytes(checked(PartitionQueryRetention.ArrayDescriptorBytes
            + (long)work.Length * PartitionQueryRetention.LeafDescriptorBytes));
        var prepared = new DistributedSearchPreparedLeaf[work.Length];
        for (var index = FirstLeaf; index < work.Length; index++)
        {
            budget.Check();
            prepared[index] = prepare(work[index], grants[grantOffset + index], budget);
        }
        var leaves = ImmutableArray.CreateBuilder<DistributedSearchLeafResultV1>(work.Length);
        var concurrency = Math.Min(policy.MaximumConcurrentPartitionLeaves, work.Length);
        for (var offset = FirstLeaf; offset < work.Length; offset += concurrency)
        {
            budget.Check();
            var actual = await PartitionQueryParallelBatch.RunAsync<DistributedSearchPreparedLeaf, DistributedSearchLeafResultV1>(
                prepared, offset, Math.Min(concurrency, work.Length - offset), execution,
                static (leaf, token) => leaf.RunAsync(token)).ConfigureAwait(false);
            for (var index = FirstLeaf; index < actual.Length; index++)
            {
                budget.Check();
                var leaf = prepared[offset + index];
                DistributedSearchLeafResultValidation.Require(leaf.ActualRequest, actual[index], budget);
                await leaf.CompleteAsync(actual[index], execution.Token).ConfigureAwait(false);
                budget.Check();
                budget.CompleteReadGrant(grants[grantOffset + offset + index]);
                var retained = new GlobalBranchByteAdmission(budget.MaximumResultBytes, budget);
                retained.Accept(NativeSerialization.Measure(actual[index]));
                leaves.Add(actual[index]);
            }
        }
        return leaves.MoveToImmutable();
    }
}
