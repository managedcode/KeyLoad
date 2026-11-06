using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Query;

internal static class SqlGraphSearchExecutor
{
    internal static async Task<GraphSearchResult> ExecuteAsync(SearchEngine search,
        string principalId, SqlGraphSearchRequest request, QueryExecutionOptions execution, CancellationToken cancellationToken,
        IResourceLifetimeWorkBinding? binding = null)
    {
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: BCL null validation accepts the request SqlGraphSearchRequest reference through its object parameter. */;
        var budget = new ReadExecutionBudget(cancellationToken);
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
