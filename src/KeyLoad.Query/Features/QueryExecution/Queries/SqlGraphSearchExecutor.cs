using KeyLoad.Core;

namespace KeyLoad.Query;

internal static class SqlGraphSearchExecutor
{
    internal static async Task<GraphSearchResult> ExecuteAsync(DatabaseEngine database, SearchEngine search,
        string principalId, SqlGraphSearchRequest request, QueryExecutionOptions execution, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var budget = new ReadExecutionBudget(database.Limits, cancellationToken: cancellationToken);
        budget.Check();
        var graphRequest = SqlGraphSearchParser.Parse(request, database.Limits, budget, execution.MaximumParameters);
        budget.Check();
        return await search.GraphSearchAsync(principalId, graphRequest, cancellationToken).ConfigureAwait(false);
    }
}
