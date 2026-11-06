using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Query.QueryExecution;

namespace KeyLoad.Query;

public sealed partial class QueryEngine
{
    /// <summary>Executes the closed KeyLoad.GraphPath statement through the canonical shortest-path operator.</summary>
    /// <param name="principalId">Persisted database principal identifier.</param>
    /// <param name="request">SQL request and exact scalar parameters.</param>
    /// <param name="cancellationToken">Cancellation across admission, parsing and execution reads.</param>
    /// <returns>The same bounded result and read cut produced by the direct graph-path request.</returns>
    public GraphShortestPathResult ShortestPathSql(string principalId, SqlGraphPathRequest request,
        CancellationToken cancellationToken = default)
    {
        return ExecuteShortestPathSql(principalId, request, cancellationToken);
    }

    private GraphShortestPathResult ExecuteShortestPathSql(string principalId, SqlGraphPathRequest request,
        CancellationToken cancellationToken, IResourceLifetimeWorkBinding? binding = null)
    {
        var budget = new ReadExecutionBudget(cancellationToken);
        if (binding is not null)
        {
            budget.AttachResourceLifetimeBinding(binding);
        }
        budget.Check();
        if (request is null)
        {
            throw Errors.Fail(ErrorCode.Validation, SqlGraphPathSyntax.WrapperDetail);
        }
        using var admission = database.AdmitQuery(cancellationToken);
        budget.Check();
        var graphRequest = SqlGraphPathParser.Parse(request, budget, execution.MaximumParameters, database.GraphExecution.MaximumLabels);
        budget.Check();
        return database.ShortestPath(principalId, graphRequest, budget);
    }
}
