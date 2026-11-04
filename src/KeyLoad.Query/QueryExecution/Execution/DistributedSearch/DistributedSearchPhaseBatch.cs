using System.Collections.Immutable;
using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.QueryExecution;

internal static class DistributedSearchPhaseBatch
{
    private const int FirstLeaf = 0;

    internal static async Task<ImmutableArray<DistributedSearchLeafResult>> RunAsync(
        ImmutableArray<DistributedSearchPhaseWork> work, IOptions<QueryExecutionOptions> options, ReadExecutionBudget budget,
        Func<DistributedSearchPhaseWork, ReadExecutionBudget, CancellationToken, Task<DistributedSearchPreparedLeaf>> prepare,
        CancellationTokenSource execution)
    {
        ArgumentNullException.ThrowIfNull(options) /* KLD0041: BCL null validation accepts the options IOptions reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(budget) /* KLD0041: BCL null validation accepts the budget ReadExecutionBudget reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(prepare) /* KLD0041: BCL null validation accepts the prepare Func reference through its object parameter. */;
        budget.Check();
        var policy = options.Value;
        policy.Validate();
        if (work.IsDefaultOrEmpty || work.Length > policy.MaximumPartitions)
        {
            throw Errors.Fail(ErrorCode.Corruption, DistributedSearchPhaseErrors.InvalidBatch);
        }
        var prepared = new DistributedSearchPreparedLeaf[work.Length];
        for (var index = FirstLeaf; index < work.Length; index++)
        {
            budget.Check();
            prepared[index] = await prepare(work[index], budget, execution.Token);
        }
        var leaves = ImmutableArray.CreateBuilder<DistributedSearchLeafResult>(work.Length);
        var concurrency = Math.Min(policy.MaximumConcurrentPartitionLeaves, work.Length);
        for (var offset = FirstLeaf; offset < work.Length; offset += concurrency)
        {
            budget.Check();
            var actual = await PartitionQueryParallelBatch.RunAsync<DistributedSearchPreparedLeaf, DistributedSearchLeafResult>(
                prepared, offset, Math.Min(concurrency, work.Length - offset), execution,
                static (leaf, token) => leaf.RunAsync(token));
            for (var index = FirstLeaf; index < actual.Length; index++)
            {
                budget.Check();
                var leaf = prepared[offset + index];
                DistributedSearchLeafResultValidation.Require(leaf.ActualRequest, actual[index], budget);
                await leaf.CompleteAsync(actual[index], execution.Token);
                budget.Check();
                leaves.Add(actual[index]);
            }
        }
        return leaves.MoveToImmutable();
    }
}
