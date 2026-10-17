using KeyLoad.Core;
using KeyLoad.Features.QueryExecution;

namespace KeyLoad.Query.Features.QueryExecution;

internal sealed class ScriptDomLoweringContext(global::Microsoft.Extensions.Options.IOptions<DatabaseLimits> limitsOptions,
    global::Microsoft.Extensions.Options.IOptions<QueryExecutionOptions> executionOptions,
    ReadExecutionBudget? budget, int maximumNodes, Dictionary<string, global::System.Text.Json.JsonElement>? parameters = null,
    int? positionalParameterCount = null)
{
    private readonly Dictionary<string, global::System.Text.Json.JsonElement>? ownedParameters = CopyParameters(parameters, budget);
    private int nodes;
    internal bool UseDocumentMetadata { get; set; } = true;
    internal Dictionary<string, global::System.Text.Json.JsonElement>? Parameters => ownedParameters;
    internal int? PositionalParameterCount => positionalParameterCount;
    internal DatabaseLimits Limits => limitsOptions.Value;
    internal QueryExecutionOptions Execution => executionOptions.Value;
    internal void Observe(int depth)
    {
        budget?.Check();
        if (depth > limitsOptions.Value.MaxQueryDepth)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ScriptDomErrors.DepthDetail);
        }
    }
    internal void Check(int depth)
    {
        Observe(depth);
        if (++nodes > maximumNodes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ScriptDomErrors.NodesDetail);
        }
    }
    private static Dictionary<string, global::System.Text.Json.JsonElement>? CopyParameters(
        Dictionary<string, global::System.Text.Json.JsonElement>? supplied, ReadExecutionBudget? work)
    {
        if (supplied is null)
        {
            return null;
        }
        var owned = new Dictionary<string, global::System.Text.Json.JsonElement>(supplied.Count, StringComparer.Ordinal);
        foreach (var entry in supplied)
        {
            work?.Check();
            if (!owned.TryAdd(entry.Key, entry.Value))
            {
                throw ScriptDomErrors.Invalid();
            }
        }
        work?.Check();
        return owned;
    }

}
