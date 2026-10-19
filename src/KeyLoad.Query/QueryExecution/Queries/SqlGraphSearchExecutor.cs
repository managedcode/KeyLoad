using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Query;

internal static class SqlGraphSearchExecutor
{
    internal static async Task<GraphSearchResult> ExecuteAsync(DatabaseEngine database, SearchEngine search,
        string principalId, SqlGraphSearchRequest request, QueryExecutionOptions execution, CancellationToken cancellationToken,
        IResourceLifetimeWorkBinding? binding = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var budget = new ReadExecutionBudget(database.OperationLimitsOptions, database.EvaluationClock, cancellationToken);
        if (binding is not null)
        {
            budget.AttachResourceLifetimeBinding(binding);
        }
        QueryResultBudgetPolicy.Constrain(budget, execution);
        budget.Check();
        var graphRequest = SqlGraphSearchParser.Parse(request, database.OperationLimitsOptions, budget, execution.MaximumParameters);
        budget.Check();
        var result = await search.GraphSearchAsync(principalId, graphRequest, budget, cancellationToken);
        if (execution.MaximumResultBytes is not null)
        {
            budget.CheckResult(result);
        }
        return result;
    }
}
