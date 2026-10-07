using KeyLoad.Core;

namespace KeyLoad.Query;

internal static class SqlGraphSearchExecutor
{
    internal static async Task<GraphSearchResult> ExecuteAsync(DatabaseEngine database, SearchEngine search,
        string principalId, SqlGraphSearchRequest request, QueryExecutionOptions execution, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var budget = new ReadExecutionBudget(database.OperationLimitsOptions, database.EvaluationClock, cancellationToken);
        QueryResultBudgetPolicy.Constrain(budget, execution);
        budget.Check();
        var graphRequest = SqlGraphSearchParser.Parse(request, database.OperationLimitsOptions, budget, execution.MaximumParameters, execution.SqlBudgetCheckInterval);
        budget.Check();
        var result = await search.GraphSearchAsync(principalId, graphRequest, budget, cancellationToken).ConfigureAwait(false);
        if (execution.MaximumResultBytes is not null)
        {
            budget.CheckResult(result);
        }
        return result;
    }
}
