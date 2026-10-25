using KeyLoad.Core;
using KeyLoad.Query.QueryExecution;

namespace KeyLoad.Query;

/// <summary>Parses the closed Q1.Search statement into the existing graph-search request.</summary>
internal static class SqlGraphSearchParser
{
    internal static GraphSearchRequest Parse(SqlGraphSearchRequest request, ReadExecutionBudget budget,
        int maximumParameters)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ValidateEnvelope(request);
        SqlGraphSearchRequestSizer.EnsureBounded(request, maximumParameters, budget.Cancellation);
        budget.Check();
        var context = new global::KeyLoad.Query.QueryExecution.ScriptDomGraphContext(request.Query, budget, SqlGraphSearchSyntax.ParameterDetail);
        var arguments = global::KeyLoad.Query.QueryExecution.ScriptDomGraphShape.Read(request.Query.Sql, context,
            global::KeyLoad.Query.QueryExecution.ScriptDomGraphSearch.Function);
        return global::KeyLoad.Query.QueryExecution.ScriptDomGraphSearch.Read(arguments, context);
    }

    private static void ValidateEnvelope(SqlGraphSearchRequest request)
    {
        if (request.Version != SqlGraphSearchSyntax.Version || request.Query is null
            || request.Query.Partition is null || request.Query.Sql is null
            || request.Query.Cursor is not null || !request.Query.AllowFullScan)
        {
            throw SqlGraphSearchSyntax.Invalid();
        }
    }
}
