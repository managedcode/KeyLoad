using KeyLoad.QueryExecution;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Query.QueryExecution;

internal static class ScriptDomSelectLowering
{
    internal static SelectQuery Read(Dom.TSqlStatement statement, int dialectVersion, ScriptDomLoweringContext context)
    {
        context.Observe(RootDepth);
        if (statement is not Dom.SelectStatement select || select.QueryExpression is not Dom.QuerySpecification query)
        {
            throw ScriptDomErrors.Unsupported();
        }
        if (select.WithCtesAndXmlNamespaces is not null || select.Into is not null || select.On is not null
            || select.ComputeClauses.Count != EmptyCount || select.OptimizerHints.Count != EmptyCount
            || query.UniqueRowFilter == Dom.UniqueRowFilter.Distinct || query.GroupByClause is not null
            || query.HavingClause is not null || query.WindowClause is not null || query.ForClause is not null
            || query.FromClause is null)
        {
            throw ScriptDomErrors.Unsupported();
        }
        var source = ScriptDomJoinLowering.Read(query.FromClause, dialectVersion, context);
        context.UseDocumentMetadata = source.Model is null && source.Join is null;
        var projection = ScriptDomProjectionLowering.Read(query.SelectElements, source.Alias, source.Join, context);
        var filter = query.WhereClause is null ? null
            : ScriptDomPredicateLowering.Read(query.WhereClause.SearchCondition, source.Alias, source.Join?.Alias, context);
        var order = ScriptDomOrderingLowering.Read(query.OrderByClause, projection, source.Alias, source.Join, context);
        var (limit, offset) = ScriptDomWindowLowering.Read(query, context);
        return new(source.Collection, source.Alias, [.. projection], filter, [.. order], limit,
            ModelSource: source.Model, InnerJoin: source.Join, Offset: offset);
    }

    private const int EmptyCount = 0;
    private const int RootDepth = 1;
}
