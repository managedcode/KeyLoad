using KeyLoad.Features.QueryExecution;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class ScriptDomJoinLowering
{
    internal static (string Collection, string? Alias, ModelQuerySource? Model, InnerJoinClause? Join) Read(
        Dom.FromClause from, int dialectVersion, ScriptDomLoweringContext context)
    {
        if (from.TableReferences.Count != SingleElement || from.PredictTableReference.Count != EmptyCount)
        { throw ScriptDomErrors.Unsupported(); }
        var table = from.TableReferences[FirstIndex];
        if (table is not Dom.QualifiedJoin join)
        {
            var source = ScriptDomSourceLowering.Read(table, context);
            return (source.Collection, source.Alias, source.Model, null);
        }
        if (dialectVersion != JoinDialectVersion || join.QualifiedJoinType != Dom.QualifiedJoinType.Inner
            || join.JoinHint != Dom.JoinHint.None || join.JoinHintColumnsCount is not null)
        { throw ScriptDomErrors.Unsupported(); }
        var left = ScriptDomSourceLowering.Read(join.FirstTableReference, context);
        var right = ScriptDomSourceLowering.Read(join.SecondTableReference, context);
        if (right.Model is not null || left.Alias is null || right.Alias is null
            || left.Alias == right.Alias || left.Collection == right.Collection)
        { throw ScriptDomErrors.Invalid(); }
        if (join.SearchCondition is not Dom.BooleanComparisonExpression condition
            || condition.ComparisonType != Dom.BooleanComparisonType.Equals
            || condition.FirstExpression is not Dom.ColumnReferenceExpression leftColumn
            || condition.SecondExpression is not Dom.ColumnReferenceExpression rightColumn)
        { throw ScriptDomErrors.Unsupported(); }
        var leftPath = Key(leftColumn, left.Alias, left.Model);
        var rightPath = Key(rightColumn, right.Alias, null);
        return (left.Collection, left.Alias, left.Model, new(right.Collection, right.Alias, leftPath, rightPath));
    }
    private static string Key(Dom.ColumnReferenceExpression column, string alias, ModelQuerySource? model)
    {
        var parts = ScriptDomNames.Column(column);
        var modelReference = model?.Kind is ModelQuerySourceKind.Events or ModelQuerySourceKind.QueueMessages
            && parts.Count == ReferenceSegments && parts[PayloadIndex] == Payload && parts[TargetIndex] == Target;
        if ((parts.Count != QualifiedSegments && !modelReference) || parts[FirstIndex] != alias)
        { throw ScriptDomErrors.Invalid(); }
        return global::KeyLoad.Core.JsonData.Path(parts.Skip(SingleElement).ToArray());
    }
    private const int PayloadIndex = 1;
    private const int TargetIndex = 2;
    private const int JoinDialectVersion = 2;
    private const int QualifiedSegments = 2;
    private const int ReferenceSegments = 3;
    private const string Payload = "payload";
    private const string Target = "to";

    private const int FirstIndex = 0;
    private const int SingleElement = 1;
    private const int EmptyCount = 0;
}
