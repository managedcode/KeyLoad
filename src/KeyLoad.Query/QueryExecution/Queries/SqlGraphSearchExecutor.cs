using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Query;

internal static class SqlGraphSearchExecutor
{
    internal static async Task<GraphSearchResult> ExecuteAsync(DatabaseEngine database, SearchEngine search,
        string principalId, SqlGraphSearchRequest request, QueryExecutionOptions execution, CancellationToken cancellationToken,
        IResourceLifetimeWorkBinding? binding = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var budget = new ReadExecutionBudget(database.EvaluationClock, cancellationToken);
        if (binding is not null)
        {
            budget.AttachResourceLifetimeBinding(binding);
        }
        budget.Check();
        var graphRequest = SqlGraphSearchParser.Parse(request, budget, execution.MaximumParameters);
        budget.Check();
        var result = await search.GraphSearchAsync(principalId, graphRequest, budget, cancellationToken);
        return result;
    }
}
