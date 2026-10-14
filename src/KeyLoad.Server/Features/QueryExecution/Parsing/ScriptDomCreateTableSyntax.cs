using Microsoft.Extensions.Options;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;
namespace KeyLoad.Server;

internal static class ScriptDomCreateTableSyntax
{
    private const int SingleName = 1;
    private const int FirstName = 0;
    private const string TemporaryPrefix = "#";
    internal static (string Name, RelationalSchema Schema) Read(Dom.CreateTableStatement statement,
        IOptions<DatabaseLimits> configured, Action checkBudget, CancellationToken token)
    {
        var context = new ScriptDomTableLoweringContext(configured, checkBudget, token);
        ScriptDomCreateTableShape.Require(statement, context);
        if (statement.SchemaObjectName.Identifiers.Count != SingleName)
        { throw ScriptDomOperationErrors.Unsupported(); }
        var name = statement.SchemaObjectName.Identifiers[FirstName].Value;
        if (name.StartsWith(TemporaryPrefix, StringComparison.Ordinal))
        { throw ScriptDomOperationErrors.Unsupported(); }
        global::KeyLoad.Core.JsonData.Identifier(name);
        var columns = new List<RelationalColumn>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        string? primary = null;
        foreach (var column in statement.Definition.ColumnDefinitions)
        {
            ScriptDomCreateTableShape.Require(column, context);
            var columnName = column.ColumnIdentifier.Value;
            global::KeyLoad.Core.JsonData.Identifier(columnName);
            if (!names.Add(columnName))
            { throw ScriptDomOperationErrors.Invalid(); }
            var type = ScriptDomTableScalarType.Read(column.DataType, context);
            var constraint = ScriptDomTableColumnConstraints.Read(column, context);
            if (constraint.Primary)
            {
                if (primary is not null || type != RelationalColumnType.Text)
                { throw ScriptDomOperationErrors.Invalid(); }
                primary = columnName;
            }
            columns.Add(new(columnName, type, constraint.Nullable));
        }
        if (primary is null)
        { throw ScriptDomOperationErrors.Invalid(); }
        context.Check();
        return (name, new(primary, [.. columns]));
    }
}
