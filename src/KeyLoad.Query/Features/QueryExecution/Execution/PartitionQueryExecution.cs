using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Query.Features.QueryExecution;

namespace KeyLoad.Query;

internal static class PartitionQueryExecution
{
    internal static PartitionQueryResultV1 ExecutePartitionQuery(this QueryEngine engine, string principalId, AstQueryRequest request,
        ImmutableArray<PartitionRef> partitions, TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var budget = new ReadExecutionBudget(engine.PartitionQueryOwner.Limits, timeProvider, cancellationToken);
        return engine.ExecutePartitionQuery(principalId, request, partitions, budget);
    }

    internal static PartitionQueryResultV1 ExecutePartitionQuery(this QueryEngine engine, string principalId, AstQueryRequest request,
        ImmutableArray<PartitionRef> partitions, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var database = engine.PartitionQueryOwner;
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);
        ArgumentNullException.ThrowIfNull(budget);
        budget.Check();
        using var admission = database.AdmitQuery(budget.Cancellation);
        budget.Check();
        var normalized = QueryValidation.Normalize(request, database.Limits);
        budget.Check();
        var identity = database.Store.Identity;
        var plan = PartitionQueryPlanFactory.Create(normalized, identity, partitions, database.Limits);
        _ = PartitionQueryPlanValidation.Validate(plan, database.Limits);
        var grants = ReserveGrants(budget, plan);
        var results = ImmutableArray.CreateBuilder<PartitionQueryLeafResultV1>(plan.Leaves.Length);
        var executor = new PartitionQueryLeafExecutor(engine, database, budget);
        for (var index = 0; index < plan.Leaves.Length; index++)
        {
            budget.Check();
            results.Add(executor.Execute(principalId, plan.Leaves[index], normalized with
            { Partition = plan.Leaves[index].Partition }, grants[index]));
        }
        var result = PartitionQueryMerge.Complete(plan, results.MoveToImmutable(), identity, database.Limits, budget);
        budget.CheckResult(result);
        return result;
    }

    private static ReadExecutionBudgetReadGrant[] ReserveGrants(ReadExecutionBudget budget,
        PartitionQueryPlanV1 plan)
    {
        var grants = new ReadExecutionBudgetReadGrant[plan.Leaves.Length];
        for (var index = 0; index < grants.Length; index++)
        {
            var leaf = plan.Leaves[index];
            grants[index] = budget.CreateReadGrant(leaf.MaxReadBytes, leaf.MaxExaminedRecords);
        }
        return grants;
    }
}
