using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Server;

internal static class ScriptDomInsertShape
{
    internal const string GraphEdges = "GRAPH_EDGES";
    internal const string QueueMessages = "QUEUE_MESSAGES";
    internal const string GraphTraverse = "GRAPH_TRAVERSE";
    internal const string Documents = "DOCUMENTS";
    internal static (string Target, Dom.QuerySpecification Query) Read(Dom.InsertStatement statement)
    {
        var insert = statement.InsertSpecification;
        if (statement.WithCtesAndXmlNamespaces is not null || statement.OptimizerHints.Count != EmptyCount
            || insert.TopRowFilter is not null || insert.OutputClause is not null || insert.OutputIntoClause is not null
            || insert.InsertOption == Dom.InsertOption.Over || insert.Target is not Dom.NamedTableReference target
            || target.Alias is not null || target.ForPath || target.TableHints.Count != EmptyCount
            || target.TableSampleClause is not null || target.TemporalClause is not null
            || insert.InsertSource is not Dom.SelectInsertSource { Select: Dom.QuerySpecification query })
        {
            throw ScriptDomOperationErrors.Unsupported();
        }
        if (query.TopRowFilter is not null || query.UniqueRowFilter == Dom.UniqueRowFilter.Distinct
            || query.WhereClause is not null || query.GroupByClause is not null || query.HavingClause is not null
            || query.WindowClause is not null || query.OrderByClause is not null || query.OffsetClause is not null
            || query.ForClause is not null || query.FromClause is null || query.FromClause.TableReferences.Count != One)
        {
            throw ScriptDomOperationErrors.Unsupported();
        }
        var name = ObjectName(target.SchemaObject);
        var columns = name.Equals(GraphEdges, StringComparison.OrdinalIgnoreCase) ? ForwardColumns
            : name.Equals(QueueMessages, StringComparison.OrdinalIgnoreCase) ? ReverseColumns
            : throw ScriptDomOperationErrors.Unsupported();
        if (insert.Columns.Count != columns.Length)
        {
            throw ScriptDomOperationErrors.Invalid();
        }
        for (var index = EmptyCount; index < columns.Length; index++)
        {
            var column = insert.Columns[index].MultiPartIdentifier;
            if (column is null || column.Identifiers.Count != One
                || !column.Identifiers[EmptyCount].Value.Equals(columns[index], StringComparison.OrdinalIgnoreCase))
            {
                throw ScriptDomOperationErrors.Unsupported();
            }
        }
        return (name, query);
    }
    internal static Dom.ScalarExpression[] Values(Dom.QuerySpecification query, int count)
    {
        if (query.SelectElements.Count != count)
        {
            throw ScriptDomOperationErrors.Invalid();
        }
        var result = new Dom.ScalarExpression[count];
        for (var index = EmptyCount; index < count; index++)
        {
            if (query.SelectElements[index] is not Dom.SelectScalarExpression { ColumnName: null } scalar)
            {
                throw ScriptDomOperationErrors.Unsupported();
            }
            result[index] = scalar.Expression;
        }
        return result;
    }
    internal static Dom.SchemaObjectFunctionTableReference Source(Dom.TableReference table, string name, int count, string? alias = null)
    {
        if (table is not Dom.SchemaObjectFunctionTableReference function || function.ForPath || function.Columns.Count != EmptyCount
            || !ObjectName(function.SchemaObject).Equals(name, StringComparison.OrdinalIgnoreCase)
            || !StringComparer.OrdinalIgnoreCase.Equals(function.Alias?.Value, alias))
        {
            throw ScriptDomOperationErrors.Unsupported();
        }
        if (function.Parameters.Count != count)
        {
            throw ScriptDomOperationErrors.Invalid();
        }
        return function;
    }
    private static string ObjectName(Dom.SchemaObjectName name)
        => name.Identifiers.Count == One ? name.Identifiers[EmptyCount].Value : throw ScriptDomOperationErrors.Unsupported();
    private static readonly string[] ForwardColumns = [CommandIdColumn, GraphColumn, EdgePrefixColumn, MaximumMessagesColumn];
    private static readonly string[] ReverseColumns = [CommandIdColumn, QueueColumn, MessagePrefixColumn];
    private const string CommandIdColumn = "command_id";
    private const string GraphColumn = "graph";
    private const string EdgePrefixColumn = "edge_prefix";
    private const string MaximumMessagesColumn = "max_messages";
    private const string QueueColumn = "queue";
    private const string MessagePrefixColumn = "message_prefix";
    private const int EmptyCount = 0;
    private const int One = 1;
}
