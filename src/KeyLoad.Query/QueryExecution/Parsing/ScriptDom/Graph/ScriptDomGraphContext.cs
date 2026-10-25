using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.QueryExecution;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Query.QueryExecution;

internal sealed class ScriptDomGraphContext(QueryRequest query, ReadExecutionBudget budget, string parameterDetail)
{
    private readonly Dictionary<string, JsonElement>? parameters = CopyParameters(query.Parameters, budget, parameterDetail);
    internal QueryRequest Query => query;
    internal string ParameterDetail => parameterDetail;
    internal ReadExecutionBudget Budget => budget;
    internal void Check(Dom.TSqlFragment fragment, int depth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        budget.Check();
        ExecutionStackGuard.Ensure();
        if (fragment is Dom.PrimaryExpression { Collation: not null })
        {
            throw ScriptDomErrors.Unsupported();
        }
    }
    internal JsonElement Parameter(Dom.VariableReference variable)
    {
        budget.Check();
        if (variable.Name.Length <= PrefixLength || variable.Name[FirstIndex] != Prefix
            || variable.Name[PrefixLength] == Prefix)
        {
            throw ScriptDomErrors.Unsupported();
        }
        var name = variable.Name[PrefixLength..];
        JsonData.Identifier(name);
        if (parameters is null || !parameters.TryGetValue(name, out var value))
        {
            throw Errors.Fail(ErrorCode.Validation, parameterDetail);
        }
        return value;
    }
    private static Dictionary<string, JsonElement>? CopyParameters(Dictionary<string, JsonElement>? supplied,
        ReadExecutionBudget work, string detail)
    {
        if (supplied is null)
        {
            return null;
        }
        var owned = new Dictionary<string, JsonElement>(supplied.Count, StringComparer.Ordinal);
        foreach (var entry in supplied)
        {
            work.Check();
            if (!owned.TryAdd(entry.Key, entry.Value))
            {
                throw Errors.Fail(ErrorCode.Validation, detail);
            }
        }
        work.Check();
        return owned;
    }
    private const char Prefix = '@';
    private const int PrefixLength = 1;
    private const int FirstIndex = 0;
}
