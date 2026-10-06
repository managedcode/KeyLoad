namespace KeyLoad.Comparisons.Targets;

/// <summary>Owns the native scalar indexes backing vector corpus readback and candidate filters.</summary>
internal static class HelixDbVectorSetup
{
    private static readonly string[] Properties = [HelixDbNativeTokens.TokenId, HelixDbNativeTokens.TokenNumber, HelixDbNativeTokens.TokenFiltered, HelixDbNativeTokens.TokenStable];

    internal static async Task CreateAsync(HttpClient http, string label, IDictionary<string, string> receipts, NativeComparisonExecutionOptions policy, TimeProvider timeProvider, CancellationToken token)
    {
        foreach (var property in Properties)
        {
            var range = property == HelixDbNativeTokens.TokenNumber;
            var definition = HelixDbDocumentAst.Index(label, property, range, unique: property == HelixDbNativeTokens.TokenId);
            receipts[property] = definition.ToJsonString();
            receipts[property] += await HelixDbIndexLifecycle.ExecuteAsync(http, definition, policy, token: token, timeProvider: timeProvider).ConfigureAwait(false);
        }
    }

    internal static async Task DropAsync(HttpClient http, string label, IEnumerable<string> properties, NativeComparisonExecutionOptions policy, TimeProvider timeProvider, CancellationToken token)
    {
        foreach (var property in properties)
        {
            await HelixDbIndexLifecycle.ExecuteAsync(http, HelixDbDocumentAst.DropIndex(label, property,
                property == HelixDbNativeTokens.TokenNumber, unique: property == HelixDbNativeTokens.TokenId), policy, token: token, timeProvider: timeProvider).ConfigureAwait(false);
        }
    }
}
