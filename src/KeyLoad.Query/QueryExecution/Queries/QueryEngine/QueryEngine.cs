using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Query.QueryExecution;

namespace KeyLoad.Query;

public sealed partial class QueryEngine
{
    /// <summary>Executes the closed Q1.GraphPath statement through the canonical shortest-path operator.</summary>
    /// <param name="principalId">Persisted database principal identifier.</param>
    /// <param name="request">Versioned SQL request and exact scalar parameters.</param>
    /// <param name="timeProvider">Optional clock for the shared parse and read deadline.</param>
    /// <param name="cancellationToken">Cancellation across admission, parsing and execution reads.</param>
    /// <returns>The same bounded result and read cut produced by the direct graph-path request.</returns>
    public GraphShortestPathResult ShortestPathSql(string principalId, SqlGraphPathRequest request,
        TimeProvider? timeProvider = null, CancellationToken cancellationToken = default)
    {
        return ExecuteShortestPathSql(principalId, request, timeProvider, cancellationToken);
    }

    private GraphShortestPathResult ExecuteShortestPathSql(string principalId, SqlGraphPathRequest request,
        TimeProvider? timeProvider, CancellationToken cancellationToken, IResourceLifetimeWorkBinding? binding = null)
    {
        var budget = new ReadExecutionBudget(database.OperationLimitsOptions, timeProvider ?? database.EvaluationClock, cancellationToken);
        if (binding is not null)
        {
            budget.AttachResourceLifetimeBinding(binding);
        }
        QueryResultBudgetPolicy.Constrain(budget, execution);
        budget.Check();
        if (request is null)
        {
            throw Errors.Fail(ErrorCode.Validation, SqlGraphPathSyntax.WrapperDetail);
        }
        using var admission = database.AdmitQuery(cancellationToken);
        budget.Check();
        var graphRequest = SqlGraphPathParser.Parse(request, database.OperationLimitsOptions, budget, execution.MaximumParameters, database.GraphExecution.MaximumLabels);
        budget.Check();
        return database.ShortestPath(principalId, graphRequest, budget);
    }
}
