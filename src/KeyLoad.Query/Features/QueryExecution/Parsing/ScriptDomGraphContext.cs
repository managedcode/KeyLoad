using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Features.QueryExecution;
using Microsoft.Extensions.Options;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Query.Features.QueryExecution;

internal sealed class ScriptDomGraphContext(QueryRequest query, IOptions<DatabaseLimits> limitsOptions, ReadExecutionBudget budget, string parameterDetail)
{
    private int nodes;
    internal QueryRequest Query => query;
    internal string ParameterDetail => parameterDetail;
    internal ReadExecutionBudget Budget => budget;
    internal void Check(Dom.TSqlFragment fragment, int depth)
    {
        budget.Check();
        if (depth > limitsOptions.Value.MaxQueryDepth)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ScriptDomErrors.DepthDetail);
        }
        if (++nodes > limitsOptions.Value.MaxQueryTokens)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ScriptDomErrors.NodesDetail);
        }
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
        if (query.Parameters is null || !query.Parameters.TryGetValue(name, out var value))
        {
            throw Errors.Fail(ErrorCode.Validation, parameterDetail);
        }
        return value;
    }
    private const char Prefix = '@';
    private const int PrefixLength = 1;
    private const int FirstIndex = 0;
}
