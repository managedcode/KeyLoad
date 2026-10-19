using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Compiles only the frozen Q1.GraphPath expression into the canonical Core request.</summary>
internal static class SqlGraphPathParser
{
    internal static GraphShortestPathRequest Parse(SqlGraphPathRequest request, IOptions<DatabaseLimits> limitsOptions,
        ReadExecutionBudget budget, int maximumParameters, int maximumLabels)
    {
        ArgumentNullException.ThrowIfNull(limitsOptions);
        var limits = limitsOptions.Value;
        limits.Validate();
        ValidateEnvelope(request);
        SqlGraphPathRequestSizer.EnsureBounded(request, limits.MaxQueryBytes, maximumParameters, budget);
        budget.Check();
        var context = new ScriptDomGraphContext(request.Query, limitsOptions, budget, SqlGraphPathSyntax.ParameterDetail);
        var arguments = ScriptDomGraphShape.Read(request.Query.Sql, limitsOptions, context, SqlGraphPathSyntax.Function);
        return ScriptDomGraphPath.Read(arguments, context, maximumLabels);
    }

    private static void ValidateEnvelope(SqlGraphPathRequest request)
    {
        if (request is null || request.Version != SqlGraphPathSyntax.Version || request.Query is null
            || request.Query.Partition is null || request.Query.Partition.TenantId is null
            || request.Query.Partition.DatabaseId is null || request.Query.Partition.TransactionDomainId is null
            || request.Query.Partition.PartitionKey is null || request.Query.Sql is null
            || request.Query.Cursor is not null || !request.Query.AllowFullScan)
        {
            throw SqlGraphPathSyntax.Invalid();
        }
    }
}
