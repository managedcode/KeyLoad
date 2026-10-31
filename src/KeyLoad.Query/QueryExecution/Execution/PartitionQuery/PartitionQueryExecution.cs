using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Query.QueryExecution;

namespace KeyLoad.Query;

internal static class PartitionQueryExecution
{
    internal static PartitionQueryResult ExecutePartitionQuery(this QueryEngine engine, string principalId, AstQueryRequest request,
        ImmutableArray<PartitionRef> partitions, TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var budget = new ReadExecutionBudget(timeProvider ?? engine.PartitionQueryOwner.EvaluationClock, cancellationToken);
        return engine.ExecutePartitionQuery(principalId, request, partitions, budget);
    }

    internal static PartitionQueryResult ExecutePartitionQuery(this QueryEngine engine, string principalId, AstQueryRequest request,
        ImmutableArray<PartitionRef> partitions, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(budget);
        var database = engine.PartitionQueryOwner;
        budget.Check();
        using var admission = database.AdmitQuery(budget.Cancellation);
        budget.Check();
        var normalized = QueryValidation.Normalize(request, engine.Execution);
        return ExecuteNormalized(engine, principalId, normalized, partitions, budget, null);
    }

    internal static PartitionQueryResult ExecutePartitionQuery(this QueryEngine engine, string principalId,
        PartitionQueryRequest request, ReadExecutionBudget budget, PhysicalShardRecord expectedOwner)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(budget);
        budget.Check();
        var prepared = PartitionQueryPublicValidation.Prepare(request, engine.Execution);
        return ExecuteNormalized(engine, principalId, prepared.Request, prepared.Partitions,
            budget, expectedOwner);
    }

    private static PartitionQueryResult ExecuteNormalized(QueryEngine engine, string principalId,
        AstQueryRequest normalized, ImmutableArray<PartitionRef> partitions, ReadExecutionBudget budget,
        PhysicalShardRecord? expectedOwner)
    {
        var database = engine.PartitionQueryOwner;
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);
        budget.Check();
        var identity = database.Store.Identity;
        var plan = PartitionQueryPlanFactory.Create(normalized, identity, partitions, engine.Execution);
        var results = ImmutableArray.CreateBuilder<PartitionQueryLeafResult>(plan.Leaves.Length);
        var executor = new PartitionQueryLeafExecutor(engine, database, budget, expectedOwner);
        foreach (var leaf in plan.Leaves)
        {
            budget.Check();
            results.Add(executor.Execute(principalId, leaf, normalized with
            {
                Partition = leaf.Partition
            }));
        }
        return PartitionQueryMerge.Complete(plan, results.MoveToImmutable(), identity, budget);
    }
}
