using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution.Execution;
using KeyLoad.Query.QueryExecution;

namespace KeyLoad.Query;

internal static class PartitionQueryExecution
{
    private const int FirstElementIndex = 0;

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
        QueryResultBudgetPolicy.Constrain(budget, engine.Execution);
        budget.Check();
        using var admission = database.AdmitQuery(budget.Cancellation);
        budget.Check();
        var normalized = QueryValidation.Normalize(request, engine.Execution);
        return ExecuteNormalized(engine, principalId, normalized, partitions, budget, null,
            measureInternalResult: true);
    }

    internal static PartitionQueryResult ExecutePartitionQuery(this QueryEngine engine, string principalId,
        PartitionQueryRequest request, ReadExecutionBudget budget, PhysicalShardRecord expectedOwner)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(budget);
        QueryResultBudgetPolicy.Constrain(budget, engine.Execution);
        budget.Check();
        var prepared = PartitionQueryPublicValidation.Prepare(request, engine.Execution);
        return ExecuteNormalized(engine, principalId, prepared.Request, prepared.Partitions,
            budget, expectedOwner, measureInternalResult: false);
    }

    private static PartitionQueryResult ExecuteNormalized(QueryEngine engine, string principalId,
        AstQueryRequest normalized, ImmutableArray<PartitionRef> partitions, ReadExecutionBudget budget,
        PhysicalShardRecord? expectedOwner, bool measureInternalResult)
    {
        var database = engine.PartitionQueryOwner;
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);
        budget.Check();
        var identity = database.Store.Identity;
        var plan = PartitionQueryPlanFactory.Create(normalized, identity, partitions, engine.Execution,
            maximumResultBytes: budget.MaximumResultBytes);
        _ = PartitionQueryPlanValidation.Validate(plan, engine.Execution);
        var grants = ReserveGrants(budget, plan);
        var results = ImmutableArray.CreateBuilder<PartitionQueryLeafResult>(plan.Leaves.Length);
        var executor = new PartitionQueryLeafExecutor(engine, database, budget, expectedOwner);
        for (var index = FirstElementIndex; index < plan.Leaves.Length; index++)
        {
            budget.Check();
            using (budget.EnterReadGrant(grants[index]))
            {
                results.Add(executor.Execute(principalId, plan.Leaves[index], normalized with
                {
                    Partition = plan.Leaves[index].Partition
                }, grants[index]));
            }
            budget.CompleteReadGrant(grants[index]);
        }
        var result = PartitionQueryMerge.Complete(plan, results.MoveToImmutable(), identity, budget, engine.Execution);
        if (measureInternalResult)
        {
            budget.CheckResult(result);
        }
        return result;
    }

    private static ReadExecutionBudgetReadGrant[] ReserveGrants(ReadExecutionBudget budget,
        PartitionQueryPlan plan)
    {
        var grants = new ReadExecutionBudgetReadGrant[plan.Leaves.Length];
        for (var index = FirstElementIndex; index < grants.Length; index++)
        {
            var leaf = plan.Leaves[index];
            grants[index] = budget.CreateReadGrant(leaf.MaxReadBytes, leaf.MaxExaminedRecords);
        }
        return grants;
    }
}
