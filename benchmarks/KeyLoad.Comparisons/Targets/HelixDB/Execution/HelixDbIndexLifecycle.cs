using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.Comparisons.Targets;
/// <summary>Waits for the provider's durable asynchronous index operation to actually succeed.</summary>
internal static class HelixDbIndexLifecycle
{
    private const string ProviderANNOpaqueTuning = "provider-execution ANN; opaque tuning";
    private const string CompletedOperation = "; completed operation=";
    private const string CompletedReceipt = "; completed receipt=";
    internal static async Task<string> ExecuteAsync(HttpClient http, JsonObject definition, ComparisonExecutionOptions policy, TimeProvider timeProvider, CancellationToken token)
    {
        using var indexDeadline = new ComparisonCancellationSource(timeProvider, token);
        indexDeadline.CancelAfter(policy.IndexBuildTimeout);
        token = indexDeadline.Token;
        using var response = await HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(definition, true), true, policy, token: token, timeProvider: timeProvider).ConfigureAwait(false);
        var receipt = Single(response.RootElement.GetProperty(HelixDbTokens.TokenRows));
        var kind = receipt.GetProperty(HelixDbTokens.TokenKind).GetString();
        if (kind != HelixDbTokens.TokenAccepted && kind != HelixDbTokens.TokenExistingOperation)
        {
            throw new ComparisonFailureException(HelixDbTokens.TokenHelixDbIndexOperationNotAccepted);
        }

        var operation = receipt.GetProperty(HelixDbTokens.TokenOperationId).GetString()!;
        var completion = await WaitAsync(http, operation, policy, token: token, timeProvider: timeProvider).ConfigureAwait(false);
        return receipt.GetRawText() + CompletedOperation + completion;
    }

    internal static async Task<VectorIndexReceipt> BuildAsync(HttpClient http, string label, int dimensions, ComparisonExecutionOptions policy, TimeProvider timeProvider, CancellationToken token)
    {
        var definition = HelixDbVectorAst.Index(label, dimensions);
        var watch = new ComparisonElapsedMeasurement(timeProvider);
        var receipt = await ExecuteAsync(http, definition, policy, token: token, timeProvider: timeProvider).ConfigureAwait(false);
        watch.Stop();
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [HelixDbTokens.TokenAlgorithm] = ProviderANNOpaqueTuning,
            [HelixDbTokens.TokenMetric] = HelixDbTokens.TokenCosine,
            [HelixDbTokens.TokenDimension] = dimensions.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [HelixDbTokens.TokenSearchConsistencyMetadata] = HelixDbTokens.TokenStrong
        };
        policy.RecordEvidence(parameters);
        return new(VectorIndexKind.RuntimeAnn, definition.ToJsonString() + CompletedReceipt + receipt, parameters, watch.Elapsed.TotalMilliseconds);
    }

    private static async Task<string> WaitAsync(HttpClient http, string operation, ComparisonExecutionOptions policy, TimeProvider timeProvider, CancellationToken token)
    {
        var started = timeProvider.GetTimestamp();
        while (true)
        {
            var ast = HelixDbProtocol.Node(HelixDbTokens.TokenGetIndexOperation, new()
            {
                [HelixDbTokens.TokenOperationId] = operation
            });
            using var response = await HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(ast, false), false, policy, token: token, timeProvider: timeProvider).ConfigureAwait(false);
            var observedOperation = Single(response.RootElement.GetProperty(HelixDbTokens.TokenRows));
            var status = observedOperation.GetProperty(HelixDbTokens.TokenStatus).GetString();
            if (status == HelixDbTokens.TokenSucceeded)
            {
                return observedOperation.GetRawText();
            }

            if (status is HelixDbTokens.TokenBlocked or HelixDbTokens.TokenAborted || timeProvider.GetElapsedTime(started) >= policy.IndexBuildTimeout)
            {
                throw new ComparisonFailureException(HelixDbTokens.TokenHelixDbIndexDidNotSucceed);
            }

            await Task.Delay(policy.IndexPollInterval, timeProvider, token).ConfigureAwait(false);
        }
    }

    private static JsonElement Single(JsonElement value) => value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 1 ? value[0] : value;
}
