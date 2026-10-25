using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Server;

/// <summary>Accepts one permanent unqualified table name before execution DROP admission.</summary>
internal static class ScriptDomDropTableSyntax
{
    private const int SingleName = 1;
    private const int FirstName = 0;
    private const string TemporaryPrefix = "#";

    internal static string Read(Dom.DropTableStatement statement,
        Action checkBudget, CancellationToken token)
    {
        var context = new ScriptDomTableLoweringContext(checkBudget, token);
        context.Check();
        if (statement.IsIfExists || statement.Objects.Count != SingleName)
        {
            throw ScriptDomOperationErrors.Unsupported();
        }
        var table = statement.Objects[FirstName];
        context.Check();
        if (table.Identifiers.Count != SingleName)
        {
            throw ScriptDomOperationErrors.Unsupported();
        }
        var name = table.Identifiers[FirstName].Value;
        if (name.StartsWith(TemporaryPrefix, StringComparison.Ordinal))
        {
            throw ScriptDomOperationErrors.Unsupported();
        }
        global::KeyLoad.Core.JsonData.Identifier(name);
        context.Check();
        return name;
    }
}
