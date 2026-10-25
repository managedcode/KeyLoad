using KeyLoad.Core;
using KeyLoad.QueryExecution;

namespace KeyLoad.Query.QueryExecution;

internal sealed class ScriptDomLoweringContext(global::Microsoft.Extensions.Options.IOptions<QueryExecutionOptions> executionOptions,
    ReadExecutionBudget? budget, Dictionary<string, global::System.Text.Json.JsonElement>? parameters = null,
    int? positionalParameterCount = null)
{
    private readonly Dictionary<string, global::System.Text.Json.JsonElement>? ownedParameters = (CopyParameters(parameters, budget)).Value;
    internal bool UseDocumentMetadata { get; set; } = true;
    internal Dictionary<string, global::System.Text.Json.JsonElement>? Parameters => ownedParameters;
    internal int? PositionalParameterCount => positionalParameterCount;
    internal QueryExecutionOptions Execution => executionOptions.Value;
    internal void Observe(int depth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        budget?.Check();
        ExecutionStackGuard.Ensure();
    }
    internal void Check(int depth) => Observe(depth);
    private static (bool HasValue, Dictionary<string, global::System.Text.Json.JsonElement>? Value) CopyParameters(
        Dictionary<string, global::System.Text.Json.JsonElement>? supplied, ReadExecutionBudget? work)
    {
        if (supplied is null)
        {
            return (false, default(Dictionary<string, global::System.Text.Json.JsonElement>?));
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
        return (true, ((Dictionary<string, global::System.Text.Json.JsonElement>?)(owned)));
    }

}
