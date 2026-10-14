using KeyLoad.Features.QueryExecution;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class ScriptDomProjectionLowering
{
    internal static List<Selection> Read(IList<Dom.SelectElement> elements, string? leftAlias,
        InnerJoinClause? join, ScriptDomLoweringContext context)
    {
        if (elements.Count == EmptyCount || elements.Count > context.Execution.MaximumProjection)
        {
            throw ScriptDomErrors.Invalid();
        }
        var selected = new List<Selection>(elements.Count);
        foreach (var element in elements)
        {
            context.Observe(RootDepth);
            if (element is Dom.SelectStarExpression star)
            {
                if (elements.Count != SingleElement || star.Qualifier is not null || join is not null)
                {
                    throw ScriptDomErrors.Unsupported();
                }
                selected.Add(new(SqlSyntax.Star, SqlSyntax.Star));
                continue;
            }
            if (element is not Dom.SelectScalarExpression scalar
                || scalar.Expression is not Dom.ColumnReferenceExpression column
                || scalar.Expression is Dom.PrimaryExpression { Collation: not null })
            {
                throw ScriptDomErrors.Unsupported();
            }
            var parts = ScriptDomNames.Column(column);
            var output = scalar.ColumnName?.Identifier?.Value
                ?? (scalar.ColumnName?.ValueExpression is Dom.StringLiteral text ? text.Value : parts[^LastSegment]);
            global::KeyLoad.Core.JsonData.Identifier(output);
            if (join is null)
            {
                selected.Add(new(ScriptDomNames.Path(parts, leftAlias, context.UseDocumentMetadata), output));
            }
            else
            {
                if (parts.Count < QualifiedSegments || parts[FirstIndex] != leftAlias && parts[FirstIndex] != join.Alias)
                {
                    throw ScriptDomErrors.Invalid();
                }
                selected.Add(new(global::KeyLoad.Core.JsonData.Path(parts.Skip(SingleElement).ToArray()), output, parts[FirstIndex]));
            }
        }
        if (selected.Select(value => value.Alias).Distinct(StringComparer.Ordinal).Count() != selected.Count)
        {
            throw ScriptDomErrors.Invalid();
        }
        return selected;
    }
    private const int LastSegment = 1;
    private const int QualifiedSegments = 2;

    private const int FirstIndex = 0;
    private const int SingleElement = 1;
    private const int EmptyCount = 0;
    private const int RootDepth = 1;
}
