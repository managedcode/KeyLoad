using KeyLoad.Core;
using KeyLoad.Query.Features.QueryExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query;

/// <summary>Parses the closed Q1.Search.v1 statement into the existing graph-search request.</summary>
internal static class SqlGraphSearchParser
{
    internal static GraphSearchRequest Parse(SqlGraphSearchRequest request, IOptions<DatabaseLimits> limitsOptions, ReadExecutionBudget budget,
        int maximumParameters)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(limitsOptions);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(limitsOptions);
        var limits = limitsOptions.Value;
        limits.Validate();
        ValidateEnvelope(request);
        SqlGraphSearchRequestSizer.EnsureBounded(request, limits.MaxQueryBytes, maximumParameters, budget.Cancellation);
        budget.Check();
        var context = new global::KeyLoad.Query.Features.QueryExecution.ScriptDomGraphContext(request.Query, limitsOptions, budget, SqlGraphSearchSyntax.ParameterDetail);
        var arguments = global::KeyLoad.Query.Features.QueryExecution.ScriptDomGraphShape.Read(request.Query.Sql, limitsOptions, context,
            global::KeyLoad.Query.Features.QueryExecution.ScriptDomGraphSearch.Function);
        return global::KeyLoad.Query.Features.QueryExecution.ScriptDomGraphSearch.Read(arguments, context);
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
