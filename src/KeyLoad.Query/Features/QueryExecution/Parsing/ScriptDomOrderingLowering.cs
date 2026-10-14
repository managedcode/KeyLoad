using System.Globalization;
using KeyLoad.Features.QueryExecution;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class ScriptDomOrderingLowering
{
    internal static List<Ordering> Read(Dom.OrderByClause? clause, List<Selection> projection,
        string? leftAlias, InnerJoinClause? join, ScriptDomLoweringContext context)
    {
        if (clause is null)
        { return []; }
        if (clause.All)
        { throw ScriptDomErrors.Unsupported(); }
        if (clause.OrderByElements.Count > context.Execution.MaximumOrdering)
        { throw ScriptDomErrors.Invalid(); }
        var ordering = new List<Ordering>(clause.OrderByElements.Count);
        foreach (var element in clause.OrderByElements)
        {
            context.Observe(RootDepth);
            var selected = Resolve(element.Expression, projection, leftAlias, join);
            ordering.Add(new(selected, element.SortOrder == Dom.SortOrder.Descending));
        }
        return ordering;
    }
    private static string Resolve(Dom.ScalarExpression expression, List<Selection> projection,
        string? leftAlias, InnerJoinClause? join)
    {
        if (expression is Dom.IntegerLiteral ordinal)
        {
            if (!int.TryParse(ordinal.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var position)
                || position < FirstOrdinal || position > projection.Count)
            { throw ScriptDomErrors.Invalid(); }
            var selected = projection[position - FirstOrdinal];
            if (selected.Path == SqlSyntax.Star)
            { throw ScriptDomErrors.Unsupported(); }
            if (join is not null && selected.SourceAlias != leftAlias)
            { throw ScriptDomErrors.Invalid(); }
            return selected.Path;
        }
        if (expression is Dom.NumericLiteral or Dom.RealLiteral
            || expression is Dom.UnaryExpression { Expression: Dom.IntegerLiteral or Dom.NumericLiteral or Dom.RealLiteral })
        { throw ScriptDomErrors.Invalid(); }
        if (expression is not Dom.ColumnReferenceExpression column)
        { throw ScriptDomErrors.Unsupported(); }
        var parts = ScriptDomNames.Column(column);
        if (parts.Count == SingleElement)
        {
            var selected = projection.FirstOrDefault(value => value.Alias == parts[FirstIndex]);
            if (selected is not null)
            {
                if (join is not null && selected.SourceAlias != leftAlias)
                { throw ScriptDomErrors.Invalid(); }
                return selected.Path;
            }
        }
        if (join is not null && (parts.Count != QualifiedSegments || parts[FirstIndex] != leftAlias))
        { throw ScriptDomErrors.Invalid(); }
        return ScriptDomNames.Path(parts, leftAlias);
    }
    private const int FirstOrdinal = 1;
    private const int QualifiedSegments = 2;

    private const int FirstIndex = 0;
    private const int SingleElement = 1;
    private const int RootDepth = 1;
}
