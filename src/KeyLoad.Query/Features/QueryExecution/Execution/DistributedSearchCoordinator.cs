using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class DistributedSearchCoordinator
{
    private const string DeadlineExceeded = "The read execution deadline is exceeded.";

    internal static async Task<DistributedSearchPageV1> ExecuteAsync(DatabaseEngine database,
        DistributedSearchRequestV1 request, IOptions<QueryExecutionOptions> options,
        Func<DistributedSearchPhaseWork, ReadExecutionBudgetReadGrant, ReadExecutionBudget, DistributedSearchPreparedLeaf> prepare,
        Func<CancellationToken, Task> sourceBarrier, TimeProvider clock, DateTimeOffset originalExpiry,
        CancellationToken original, Func<CancellationToken, Task>? statisticsCaptured = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(prepare);
        ArgumentNullException.ThrowIfNull(sourceBarrier);
        ArgumentNullException.ThrowIfNull(clock);
        var budget = new ReadExecutionBudget(database.OperationLimitsOptions, clock, original);
        QueryResultBudgetPolicy.Constrain(budget, options.Value);
        budget.ConstrainLifetime(originalExpiry);
        using var admission = database.AdmitQuery(original);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(database.Limits.QueryDeadlineSeconds), clock);
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(original, deadline.Token);
        using var stage = budget.EnterStageCancellation(execution.Token);
        try
        {
            var partitions = DistributedSearchValidation.Require(request, database.Limits, options, budget);
            var grants = DistributedSearchGrants.Reserve(partitions.Length, budget);
            return await new DistributedSearchOperation(database, request, partitions, options, budget, grants,
                prepare, sourceBarrier, execution, statisticsCaptured).RunAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (original.IsCancellationRequested)
        { original.ThrowIfCancellationRequested(); throw; }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, DeadlineExceeded); }
        catch (Exception cause) when (DistributedSearchDeadlineFailure.IsOwnedDeadline(cause, deadline.Token, original))
        { throw Errors.Fail(ErrorCode.BudgetExceeded, DeadlineExceeded, cause); }
    }
}
