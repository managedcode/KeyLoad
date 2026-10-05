using KeyLoad.Core;
using KeyLoad.Query.Features.QueryExecution;

namespace KeyLoad.Query;

public sealed partial class QueryEngine
{
    /// <summary>Executes the closed Q1.GraphPath.v1 statement through the canonical shortest-path operator.</summary>
    /// <param name="principalId">Persisted database principal identifier.</param>
    /// <param name="request">Versioned SQL request and exact scalar parameters.</param>
    /// <param name="timeProvider">Optional clock for the shared parse and read deadline.</param>
    /// <param name="cancellationToken">Cancellation across admission, parsing and native reads.</param>
    /// <returns>The same bounded result and read cut produced by the direct graph-path request.</returns>
    public GraphShortestPathResult ShortestPathSql(string principalId, SqlGraphPathRequest request,
        TimeProvider? timeProvider = null, CancellationToken cancellationToken = default)
    {
        var budget = new ReadExecutionBudget(database.Limits, timeProvider ?? database.EvaluationClock, cancellationToken);
        budget.Check();
        if (request is null)
        {
            throw Errors.Fail(ErrorCode.Validation, SqlGraphPathSyntax.WrapperDetail);
        }
        using var admission = database.AdmitQuery(cancellationToken);
        budget.Check();
        var graphRequest = SqlGraphPathParser.Parse(request, database.Limits, budget, execution.MaximumParameters, database.GraphExecution.MaximumLabels);
        budget.Check();
        return database.ShortestPath(principalId, graphRequest, budget);
    }
}
