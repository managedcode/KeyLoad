using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Query.QueryExecution;

/// <summary>Uses the existing execution plan and merge with authenticated, individually admitted owner leaves.</summary>
internal static partial class PartitionQueryRemoteExecution
{
    private const int FirstLeafIndex = 0;

    internal static async Task<PartitionQueryPage> ExecuteAsync(QueryEngine engine,
        PartitionQueryRequest request,
        Func<PartitionQueryLeafPlan, ReadExecutionBudget, CancellationToken, Task<PartitionQueryPreparedLeaf>> prepare,
        Func<CancellationToken, Task> revalidate, TimeProvider clock, DateTimeOffset originalExpiry,
        CancellationToken cancellationToken, IResourceLifetimeWorkBinding? binding = null)
    {
        var database = engine.PartitionQueryOwner;
        var budget = new ReadExecutionBudget(clock, cancellationToken);
        if (binding is not null)
        {
            budget.AttachResourceLifetimeBinding(binding);
        }
        budget.ConstrainLifetime(originalExpiry);
        budget.Check();
        using var admission = database.AdmitQuery(cancellationToken);
        var prepared = PartitionQueryPublicValidation.Prepare(request, engine.Execution);
        var identity = database.Store.Identity;
        var plan = PartitionQueryPlanFactory.Create(prepared.Request, identity, prepared.Partitions, engine.Execution);
        var preparedLeaves = new PartitionQueryPreparedLeaf[plan.Leaves.Length];
        for (var index = FirstLeafIndex; index < preparedLeaves.Length; index++)
        {
            budget.Check();
            preparedLeaves[index] = await prepare(plan.Leaves[index], budget, cancellationToken).ConfigureAwait(true);
        }
        var leaves = ImmutableArray.CreateBuilder<PartitionQueryLeafResult>(plan.Leaves.Length);
        var owners = ImmutableArray.CreateBuilder<PhysicalShardRecord>(plan.Leaves.Length);
        var concurrency = Math.Min(engine.Execution.MaximumConcurrentPartitionLeaves, plan.Leaves.Length);
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        for (var offset = FirstLeafIndex; offset < plan.Leaves.Length; offset += concurrency)
        {
            budget.Check();
            var actual = await PartitionQueryParallelBatch.RunAsync(preparedLeaves, offset,
                Math.Min(concurrency, plan.Leaves.Length - offset), execution).ConfigureAwait(true);
            for (var index = FirstLeafIndex; index < actual.Length; index++)
            {
                await preparedLeaves[offset + index].CompleteAsync(actual[index], cancellationToken).ConfigureAwait(true);
                leaves.Add(actual[index].Result);
                owners.Add(actual[index].Owner);
            }
        }
        await revalidate(cancellationToken).ConfigureAwait(true);
        budget.Check();
        var result = PartitionQueryMerge.Complete(plan, leaves.MoveToImmutable(), identity, budget, owners.MoveToImmutable());
        return PartitionQueryPublicMapper.MapPublic(result, budget);
    }
}
