using KeyLoad.Core;
using KeyLoad.Features.QueryExecution;

namespace KeyLoad.Query.Features.QueryExecution;

internal sealed class ScriptDomLoweringContext(global::Microsoft.Extensions.Options.IOptions<DatabaseLimits> limitsOptions,
    global::Microsoft.Extensions.Options.IOptions<QueryExecutionOptions> executionOptions,
    ReadExecutionBudget? budget, int maximumNodes, Dictionary<string, global::System.Text.Json.JsonElement>? parameters = null,
    int? positionalParameterCount = null)
{
    private int nodes;
    internal Dictionary<string, global::System.Text.Json.JsonElement>? Parameters => parameters;
    internal int? PositionalParameterCount => positionalParameterCount;
    internal DatabaseLimits Limits => limitsOptions.Value;
    internal QueryExecutionOptions Execution => executionOptions.Value;
    internal void Observe(int depth)
    {
        budget?.Check();
        if (depth > limitsOptions.Value.MaxQueryDepth)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, ScriptDomErrors.DepthDetail); }
    }
    internal void Check(int depth)
    {
        Observe(depth);
        if (++nodes > maximumNodes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, ScriptDomErrors.NodesDetail); }
    }
}
