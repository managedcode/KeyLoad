namespace KeyLoad.Comparisons.Targets;

/// <summary>Owns the execution scalar indexes backing vector corpus readback and candidate filters.</summary>
internal static class HelixDbVectorSetup
{
    private static readonly string[] Properties = [HelixDbTokens.TokenId, HelixDbTokens.TokenNumber, HelixDbTokens.TokenFiltered, HelixDbTokens.TokenStable];

    internal static async Task CreateAsync(HttpClient http, string label, IDictionary<string, string> receipts, ComparisonExecutionOptions policy, TimeProvider timeProvider, CancellationToken token)
    {
        foreach (var property in Properties)
        {
            var range = property == HelixDbTokens.TokenNumber;
            var definition = HelixDbDocumentAst.Index(label, property, range, unique: property == HelixDbTokens.TokenId);
            receipts[property] = definition.ToJsonString();
            receipts[property] += await HelixDbIndexLifecycle.ExecuteAsync(http, definition, policy, token: token, timeProvider: timeProvider).ConfigureAwait(false);
        }
    }

    internal static async Task DropAsync(HttpClient http, string label, IEnumerable<string> properties, ComparisonExecutionOptions policy, TimeProvider timeProvider, CancellationToken token)
    {
        foreach (var property in properties)
        {
            await HelixDbIndexLifecycle.ExecuteAsync(http, HelixDbDocumentAst.DropIndex(label, property,
                property == HelixDbTokens.TokenNumber, unique: property == HelixDbTokens.TokenId), policy, token: token, timeProvider: timeProvider).ConfigureAwait(false);
        }
    }
}
