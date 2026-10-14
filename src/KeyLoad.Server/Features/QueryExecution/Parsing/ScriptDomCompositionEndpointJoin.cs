using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Server;

internal static class ScriptDomCompositionEndpointJoin
{
    internal static (string Queue, string From, string To) Read(Dom.TableReference table, ScriptDomCompositionParameters parameters)
    {
        var outer = Join(table);
        var inner = Join(outer.FirstTableReference);
        var queue = ScriptDomInsertShape.Source(inner.FirstTableReference, ScriptDomInsertShape.QueueMessages, One, QueueAlias);
        var from = ScriptDomInsertShape.Source(inner.SecondTableReference, ScriptDomInsertShape.Documents, One, FromAlias);
        var to = ScriptDomInsertShape.Source(outer.SecondTableReference, ScriptDomInsertShape.Documents, One, ToAlias);
        Link(inner.SearchCondition, FromReference, FromAlias);
        Link(outer.SearchCondition, ToReference, ToAlias);
        return (parameters.Read<string>(queue.Parameters[FirstIndex]), parameters.Read<string>(from.Parameters[FirstIndex]),
            parameters.Read<string>(to.Parameters[FirstIndex]));
    }
    private static Dom.QualifiedJoin Join(Dom.TableReference table)
    {
        if (table is not Dom.QualifiedJoin join || join.QualifiedJoinType != Dom.QualifiedJoinType.Inner
            || join.JoinHint != Dom.JoinHint.None || join.JoinHintColumnsCount is not null)
        { throw ScriptDomOperationErrors.Unsupported(); }
        return join;
    }
    private static void Link(Dom.BooleanExpression expression, string queueField, string entityAlias)
    {
        if (expression is not Dom.BooleanComparisonExpression { ComparisonType: Dom.BooleanComparisonType.Equals } comparison
            || !Column(comparison.FirstExpression, QueueAlias, queueField)
            || !Column(comparison.SecondExpression, entityAlias, EntityReference))
        { throw ScriptDomOperationErrors.Unsupported(); }
    }
    private static bool Column(Dom.ScalarExpression expression, string alias, string field)
        => expression is Dom.ColumnReferenceExpression { Collation: null, MultiPartIdentifier: { } name }
            && name.Identifiers.Count == QualifiedNameCount
            && name.Identifiers[AliasIndex].Value.Equals(alias, StringComparison.OrdinalIgnoreCase)
            && name.Identifiers[FieldIndex].Value.Equals(field, StringComparison.OrdinalIgnoreCase);
    private const string QueueAlias = "q";
    private const string FromAlias = "f";
    private const string ToAlias = "t";
    private const string FromReference = "from_entity_ref";
    private const string ToReference = "to_entity_ref";
    private const string EntityReference = "entity_ref";
    private const int One = 1;
    private const int FirstIndex = 0;
    private const int QualifiedNameCount = 2;
    private const int AliasIndex = 0;
    private const int FieldIndex = 1;
}
