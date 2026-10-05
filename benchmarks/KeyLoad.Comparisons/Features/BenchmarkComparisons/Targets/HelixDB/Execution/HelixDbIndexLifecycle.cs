using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.Comparisons.Targets;
/// <summary>Waits for the provider's durable asynchronous index operation to actually succeed.</summary>
internal static class HelixDbIndexLifecycle
{
    private const string ProviderNativeANNOpaqueTuning = "provider-native ANN; opaque tuning";
    private const string CompletedReceipt = "; completed receipt=";
    private const int SingleResultCardinality = 1;
    private const int EmptyResultCount = 0;
    internal static async Task<string> ExecuteAsync(HttpClient http, JsonObject definition, NativeComparisonExecutionOptions policy, CancellationToken token)
    {
        using var indexDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        indexDeadline.CancelAfter(policy.IndexBuildTimeout);
        token = indexDeadline.Token;
        using var response = await HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(definition, true), true, policy, token).ConfigureAwait(false);
        var receipt = Single(response.RootElement.GetProperty(HelixDbNativeTokens.TokenRows));
        var kind = receipt.GetProperty(HelixDbNativeTokens.TokenKind).GetString();
        if (kind != HelixDbNativeTokens.TokenAccepted && kind != HelixDbNativeTokens.TokenExistingOperation)
        {
            throw new ComparisonFailureException(HelixDbNativeTokens.TokenHelixDbIndexOperationNotAccepted);
        }

        var operation = receipt.GetProperty(HelixDbNativeTokens.TokenOperationId).GetString()!;
        await WaitAsync(http, operation, policy, token).ConfigureAwait(false);
        return receipt.GetRawText();
    }

    internal static async Task<VectorIndexReceipt> BuildAsync(HttpClient http, string label, int dimensions, NativeComparisonExecutionOptions policy, CancellationToken token)
    {
        var definition = HelixDbVectorAst.Index(label, dimensions);
        var watch = Stopwatch.StartNew();
        var receipt = await ExecuteAsync(http, definition, policy, token).ConfigureAwait(false);
        watch.Stop();
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [HelixDbNativeTokens.TokenAlgorithm] = ProviderNativeANNOpaqueTuning,
            [HelixDbNativeTokens.TokenMetric] = HelixDbNativeTokens.TokenCosine,
            [HelixDbNativeTokens.TokenDimension] = dimensions.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [HelixDbNativeTokens.TokenSearchConsistencyMetadata] = HelixDbNativeTokens.TokenStrong
        };
        policy.RecordEvidence(parameters);
        return new(VectorIndexKind.NativeAnn, definition.ToJsonString() + CompletedReceipt + receipt, parameters, watch.Elapsed.TotalMilliseconds);
    }

    private static async Task WaitAsync(HttpClient http, string operation, NativeComparisonExecutionOptions policy, CancellationToken token)
    {
        var deadline = TimeProvider.System.GetUtcNow() + policy.IndexBuildTimeout;
        while (true)
        {
            var ast = HelixDbProtocol.Node(HelixDbNativeTokens.TokenGetIndexOperation, new() { [HelixDbNativeTokens.TokenOperationId] = operation });
            using var response = await HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(ast, false), false, policy, token).ConfigureAwait(false);
            var status = Single(response.RootElement.GetProperty(HelixDbNativeTokens.TokenRows)).GetProperty(HelixDbNativeTokens.TokenStatus).GetString();
            if (status == HelixDbNativeTokens.TokenSucceeded)
            {
                return;
            }

            if (status is HelixDbNativeTokens.TokenBlocked or HelixDbNativeTokens.TokenAborted || TimeProvider.System.GetUtcNow() >= deadline)
            {
                throw new ComparisonFailureException(HelixDbNativeTokens.TokenHelixDbIndexDidNotSucceed);
            }

            await Task.Delay(policy.IndexPollInterval, token).ConfigureAwait(false);
        }
    }

    private static JsonElement Single(JsonElement value) => value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == SingleResultCardinality ? value[EmptyResultCount] : value;
}
