using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Uses the existing native plan and merge with authenticated, individually admitted owner leaves.</summary>
internal static class PartitionQueryRemoteExecution
{
    private const int FirstLeafIndex = 0;
    private const string DeadlineExceededDetail = "The read execution deadline is exceeded.";
    internal static async Task<PartitionQueryPageV1> ExecuteAsync(QueryEngine engine,
        PartitionQueryRequestV1 request,
        Func<PartitionQueryLeafPlanV1, ReadExecutionBudgetReadGrant, ReadExecutionBudget, PartitionQueryPreparedLeaf> prepare,
        Func<CancellationToken, Task> revalidate, TimeProvider clock, CancellationToken cancellationToken)
    {
        var database = engine.PartitionQueryOwner;
        var budget = new ReadExecutionBudget(database.OperationLimitsOptions, clock, cancellationToken);
        QueryResultBudgetPolicy.Constrain(budget, engine.Execution);
        budget.Check();
        using var admission = database.AdmitQuery(cancellationToken);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(database.Limits.QueryDeadlineSeconds), clock);
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        using var stage = budget.EnterStageCancellation(execution.Token);
        try
        {
            var prepared = PartitionQueryPublicValidation.Prepare(request, database.Limits, engine.Execution);
            var identity = database.Store.Identity;
            var plan = PartitionQueryPlanFactory.Create(prepared.Request, identity, prepared.Partitions,
                database.Limits, engine.Execution, budget.MaximumResultBytes);
            _ = PartitionQueryPlanValidation.Validate(plan, database.Limits, engine.Execution);
            var grants = Reserve(plan, budget);
            var preparedLeaves = new PartitionQueryPreparedLeaf[plan.Leaves.Length];
            for (var index = FirstLeafIndex; index < preparedLeaves.Length; index++)
            { budget.Check(); preparedLeaves[index] = prepare(plan.Leaves[index], grants[index], budget); }
            var leaves = ImmutableArray.CreateBuilder<PartitionQueryLeafResultV1>(plan.Leaves.Length);
            var owners = ImmutableArray.CreateBuilder<PhysicalShardRecord>(plan.Leaves.Length);
            var concurrency = Math.Min(engine.Execution.MaximumConcurrentPartitionLeaves, plan.Leaves.Length);
            for (var offset = FirstLeafIndex; offset < plan.Leaves.Length; offset += concurrency)
            {
                budget.Check();
                var actual = await PartitionQueryParallelBatch.RunAsync(preparedLeaves, offset,
                    Math.Min(concurrency, plan.Leaves.Length - offset), execution).ConfigureAwait(false);
                for (var index = FirstLeafIndex; index < actual.Length; index++)
                {
                    budget.Check();
                    await preparedLeaves[offset + index].CompleteAsync(actual[index], execution.Token).ConfigureAwait(false);
                    budget.Check();
                    leaves.Add(actual[index].Result);
                    owners.Add(actual[index].Owner);
                }
            }
            await RevalidateAsync(revalidate, deadline, execution.Token, cancellationToken).ConfigureAwait(false);
            budget.Check();
            for (var index = FirstLeafIndex; index < leaves.Count; index++)
            { leaves[index] = leaves[index] with { ReadBytes = grants[index].ReadBytes, ExaminedRecords = grants[index].ExaminedRecords }; }
            var result = PartitionQueryMerge.Complete(plan, leaves.MoveToImmutable(), identity,
                database.Limits, budget, engine.Execution, owners.MoveToImmutable());
            return PartitionQueryPublicMapper.MapPublic(result, database.Limits, budget);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { cancellationToken.ThrowIfCancellationRequested(); throw; }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, DeadlineExceededDetail); }
    }

    private static async Task RevalidateAsync(Func<CancellationToken, Task> revalidate,
        CancellationTokenSource deadline, CancellationToken execution, CancellationToken original)
    {
        try
        { await revalidate(execution).ConfigureAwait(false); }
        catch (OperationCanceledException) when (original.IsCancellationRequested)
        { original.ThrowIfCancellationRequested(); throw; }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, DeadlineExceededDetail); }
    }

    private static ReadExecutionBudgetReadGrant[] Reserve(PartitionQueryPlanV1 plan, ReadExecutionBudget budget)
    {
        var grants = new ReadExecutionBudgetReadGrant[plan.Leaves.Length];
        for (var index = FirstLeafIndex; index < plan.Leaves.Length; index++)
        { grants[index] = budget.CreateReadGrant(plan.Leaves[index].MaxReadBytes, plan.Leaves[index].MaxExaminedRecords); }
        return grants;
    }
}
