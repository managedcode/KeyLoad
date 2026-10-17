using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;
namespace KeyLoad.Server;

internal static class ScriptDomValuesInsertShape
{
    internal static (string Table, string[] Columns, Dom.ValuesInsertSource Values) Read(Dom.InsertStatement statement,
        Action checkBudget, CancellationToken token)
    {
        checkBudget();
        token.ThrowIfCancellationRequested();
        var insert = statement.InsertSpecification;
        if (statement.WithCtesAndXmlNamespaces is not null || statement.OptimizerHints.Count != Empty
            || insert.TopRowFilter is not null || insert.OutputClause is not null || insert.OutputIntoClause is not null
            || insert.InsertOption == Dom.InsertOption.Over || insert.Target is not Dom.NamedTableReference target
            || target.Alias is not null || target.ForPath || target.TableHints.Count != Empty
            || target.TableSampleClause is not null || target.TemporalClause is not null
            || target.SchemaObject.Identifiers.Count != One
            || insert.InsertSource is not Dom.ValuesInsertSource { IsDefaultValues: false } values)
        {
            throw ScriptDomOperationErrors.Unsupported();
        }
        if (insert.Columns.Count == Empty || values.RowValues.Count == Empty)
        {
            throw ScriptDomOperationErrors.Invalid();
        }
        var columns = new string[insert.Columns.Count];
        var unique = new HashSet<string>(StringComparer.Ordinal);
        for (var index = Empty; index < columns.Length; index++)
        {
            checkBudget();
            token.ThrowIfCancellationRequested();
            var column = insert.Columns[index];
            if (column.ColumnType != Dom.ColumnType.Regular || column.MultiPartIdentifier is not { Identifiers.Count: One } name)
            {
                throw ScriptDomOperationErrors.Unsupported();
            }
            var selected = name.Identifiers[Empty].Value;
            global::KeyLoad.Core.JsonData.Identifier(selected);
            if (!unique.Add(selected))
            {
                throw ScriptDomOperationErrors.Invalid();
            }
            columns[index] = selected;
        }
        var table = target.SchemaObject.Identifiers[Empty].Value;
        global::KeyLoad.Core.JsonData.Identifier(table);
        return (table, columns, values);
    }
    private const int Empty = 0;
    private const int One = 1;
}
