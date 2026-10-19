using Microsoft.Extensions.Options;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Server;

/// <summary>Accepts one permanent unqualified table name before native DROP admission.</summary>
internal static class ScriptDomDropTableSyntax
{
    private const int SingleName = 1;
    private const int FirstName = 0;
    private const string TemporaryPrefix = "#";

    internal static string Read(Dom.DropTableStatement statement, IOptions<DatabaseLimits> configured,
        Action checkBudget, CancellationToken token)
    {
        var context = new ScriptDomTableLoweringContext(configured, checkBudget, token);
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
