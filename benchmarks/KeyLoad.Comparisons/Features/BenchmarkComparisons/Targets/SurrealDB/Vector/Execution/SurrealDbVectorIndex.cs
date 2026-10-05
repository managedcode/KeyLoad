using System.Globalization;
using System.Diagnostics;

namespace KeyLoad.Comparisons.Targets;
/// <summary>Observes native HNSW backfill completion and records its actual definition.</summary>
internal static class SurrealDbVectorIndex
{
    private const string NativeINFOFORTABLEFormat = "INFO FOR TABLE {0};";
    private const string SurrealDbNativeIndexDefinitionMismatch = "SurrealDbNativeIndexDefinitionMismatch";
    private const string NativeINFOFORINDEXONTABLEFormat = "INFO FOR INDEX {0} ON TABLE {1};";
    private const string NativeHnsw = "HNSW";
    private const string NativeCosine = "COSINE";
    private const string NativeFloat32 = "F32";
    private const string IndexesField = "indexes";
    private const string ReadyState = "ready";
    private const string FailedState = "failed";
    private const string ErrorState = "error";
    private const int Neighbors = 16;
    private const int ConstructionEf = 200;
    private const int SearchEf = 200;
    internal static async Task<VectorIndexReceipt> BuildAsync(HttpClient http, string table, string index, VectorComparisonProfile profile, NativeComparisonExecutionOptions policy, CancellationToken cancellationToken)
    {
        using var indexDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        indexDeadline.CancelAfter(policy.IndexBuildTimeout);
        cancellationToken = indexDeadline.Token;
        var definition = SurrealDbVectorProtocol.HnswIndex(index, table, profile.Dimensions, ConstructionEf, Neighbors);
        var timer = Stopwatch.StartNew();
        await SurrealDbSqlTransport.ExecuteAsync(http, definition, policy, cancellationToken).ConfigureAwait(false);
        await WaitAsync(http, table, index, policy, cancellationToken).ConfigureAwait(false);
        timer.Stop();
        using var nativeInfo = await SurrealDbSqlTransport.QueryAsync(http, string.Format(System.Globalization.CultureInfo.InvariantCulture, NativeINFOFORTABLEFormat, table), policy, cancellationToken).ConfigureAwait(false);
        var actualDefinition = SurrealDbVectorProtocol.ReadRequiredString(SurrealDbVectorProtocol.SingleResult(nativeInfo.RootElement).GetProperty(IndexesField), index);
        if (!actualDefinition.Contains(NativeHnsw, StringComparison.OrdinalIgnoreCase) || !actualDefinition.Contains(NativeCosine, StringComparison.OrdinalIgnoreCase) || !actualDefinition.Contains(NativeFloat32, StringComparison.OrdinalIgnoreCase))
        {
            throw new ComparisonFailureException(SurrealDbNativeIndexDefinitionMismatch);
        }

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SurrealDbNativeTokens.TokenAlgorithm] = SurrealDbNativeTokens.TokenHNSW,
            [SurrealDbNativeTokens.TokenDimension] = profile.Dimensions.ToString(CultureInfo.InvariantCulture),
            [SurrealDbNativeTokens.TokenDistance] = SurrealDbNativeTokens.TokenCOSINE,
            [SurrealDbNativeTokens.TokenEfConstruction] = ConstructionEf.ToString(CultureInfo.InvariantCulture),
            [SurrealDbNativeTokens.TokenM] = Neighbors.ToString(CultureInfo.InvariantCulture),
            [SurrealDbNativeTokens.TokenEfSearch] = SearchEf.ToString(CultureInfo.InvariantCulture)
        };
        policy.RecordEvidence(parameters);
        return new(VectorIndexKind.Hnsw, actualDefinition, parameters, timer.Elapsed.TotalMilliseconds);
    }

    private static async Task WaitAsync(HttpClient http, string table, string index, NativeComparisonExecutionOptions policy, CancellationToken cancellationToken)
    {
        var deadline = TimeProvider.System.GetUtcNow() + policy.IndexBuildTimeout;
        while (true)
        {
            using var response = await SurrealDbSqlTransport.QueryAsync(http, string.Format(System.Globalization.CultureInfo.InvariantCulture, NativeINFOFORINDEXONTABLEFormat, index, table), policy, cancellationToken).ConfigureAwait(false);
            var info = SurrealDbVectorProtocol.SingleResult(response.RootElement);
            if (!info.TryGetProperty(SurrealDbNativeTokens.TokenBuilding, out var building))
            {
                throw new InvalidDataException(SurrealDbNativeTokens.TokenSurrealDbInvalidIndexStatus);
            }

            var state = SurrealDbVectorProtocol.ReadRequiredString(building, SurrealDbNativeTokens.TokenStatus);
            if (state.Equals(ReadyState, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (state is FailedState or ErrorState || TimeProvider.System.GetUtcNow() >= deadline)
            {
                throw new InvalidDataException(SurrealDbNativeTokens.TokenSurrealDbHnswBuildDidNotBecomeReady);
            }

            await Task.Delay(policy.IndexPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }
}
