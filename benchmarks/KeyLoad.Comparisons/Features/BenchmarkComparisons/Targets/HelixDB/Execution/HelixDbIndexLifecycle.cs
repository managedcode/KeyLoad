using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.Comparisons.Targets;
/// <summary>Waits for the provider's durable asynchronous index operation to actually succeed.</summary>
internal static class HelixDbIndexLifecycle
{
    private const string ProviderNativeANNOpaqueTuning = "provider-native ANN; opaque tuning";
    private const string CompletedOperation = "; completed operation=";
    private const string CompletedReceipt = "; completed receipt=";
    private const int SingleResultCardinality = 1;
    private const int EmptyResultCount = 0;
    internal static async Task<string> ExecuteAsync(HttpClient http, JsonObject definition, NativeComparisonExecutionOptions policy, TimeProvider timeProvider, CancellationToken token)
    {
        using var indexDeadline = new ComparisonCancellationSource(timeProvider, token);
        indexDeadline.CancelAfter(policy.IndexBuildTimeout);
        token = indexDeadline.Token;
        using var response = await HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(definition, true), true, policy, token: token, timeProvider: timeProvider).ConfigureAwait(false);
        var receipt = Single(response.RootElement.GetProperty(HelixDbNativeTokens.TokenRows));
        var kind = receipt.GetProperty(HelixDbNativeTokens.TokenKind).GetString();
        if (kind != HelixDbNativeTokens.TokenAccepted && kind != HelixDbNativeTokens.TokenExistingOperation)
        {
            throw new ComparisonFailureException(HelixDbNativeTokens.TokenHelixDbIndexOperationNotAccepted);
        }

        var operation = receipt.GetProperty(HelixDbNativeTokens.TokenOperationId).GetString()!;
        var completion = await WaitAsync(http, operation, policy, token: token, timeProvider: timeProvider).ConfigureAwait(false);
        return receipt.GetRawText() + CompletedOperation + completion;
    }

    internal static async Task<VectorIndexReceipt> BuildAsync(HttpClient http, string label, int dimensions, NativeComparisonExecutionOptions policy, TimeProvider timeProvider, CancellationToken token)
    {
        var definition = HelixDbVectorAst.Index(label, dimensions);
        var watch = new ComparisonElapsedMeasurement(timeProvider);
        var receipt = await ExecuteAsync(http, definition, policy, token: token, timeProvider: timeProvider).ConfigureAwait(false);
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

    private static async Task<string> WaitAsync(HttpClient http, string operation, NativeComparisonExecutionOptions policy, TimeProvider timeProvider, CancellationToken token)
    {
        var started = timeProvider.GetTimestamp();
        while (true)
        {
            var ast = HelixDbProtocol.Node(HelixDbNativeTokens.TokenGetIndexOperation, new() { [HelixDbNativeTokens.TokenOperationId] = operation });
            using var response = await HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(ast, false), false, policy, token: token, timeProvider: timeProvider).ConfigureAwait(false);
            var observedOperation = Single(response.RootElement.GetProperty(HelixDbNativeTokens.TokenRows));
            var status = observedOperation.GetProperty(HelixDbNativeTokens.TokenStatus).GetString();
            if (status == HelixDbNativeTokens.TokenSucceeded)
            {
                return observedOperation.GetRawText();
            }

            if (status is HelixDbNativeTokens.TokenBlocked or HelixDbNativeTokens.TokenAborted || timeProvider.GetElapsedTime(started) >= policy.IndexBuildTimeout)
            {
                throw new ComparisonFailureException(HelixDbNativeTokens.TokenHelixDbIndexDidNotSucceed);
            }

            await Task.Delay(policy.IndexPollInterval, timeProvider, token).ConfigureAwait(false);
        }
    }

    private static JsonElement Single(JsonElement value) => value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == SingleResultCardinality ? value[EmptyResultCount] : value;
}
