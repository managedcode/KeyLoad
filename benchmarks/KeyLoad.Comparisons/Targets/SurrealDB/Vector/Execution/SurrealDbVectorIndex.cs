using System.Globalization;

namespace KeyLoad.Comparisons.Targets;
/// <summary>Observes execution HNSW backfill completion and records its actual definition.</summary>
internal static class SurrealDbVectorIndex
{
    private const string INFOFORTABLEFormatTemplate = "INFO FOR TABLE {0};";
    private static readonly System.Text.CompositeFormat INFOFORTABLEFormat = System.Text.CompositeFormat.Parse(INFOFORTABLEFormatTemplate);
    private const string SurrealDbIndexDefinitionMismatch = "SurrealDbIndexDefinitionMismatch";
    private const string INFOFORINDEXONTABLEFormatTemplate = "INFO FOR INDEX {0} ON TABLE {1};";
    private static readonly System.Text.CompositeFormat INFOFORINDEXONTABLEFormat = System.Text.CompositeFormat.Parse(INFOFORINDEXONTABLEFormatTemplate);
    private const string DimensionSetting = "DIMENSION";
    private const string ConstructionSetting = "EFC";
    private const string NeighborsSetting = "M";
    private const string RuntimeHnsw = "HNSW";
    private const string RuntimeCosine = "COSINE";
    private const string Float32 = "F32";
    private const string IndexesField = "indexes";
    private const string ReadyState = "ready";
    private const string FailedState = "failed";
    private const string AbortedState = "aborted";
    private const string ErrorState = "error";
    private const int Neighbors = 16;
    private const int ConstructionEf = 200;
    private const int SearchEf = 200;
    internal static async Task<VectorIndexReceipt> BuildAsync(HttpClient http, string table, string index, VectorComparisonProfile profile, ComparisonExecutionOptions policy, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        using var indexDeadline = new ComparisonCancellationSource(timeProvider, cancellationToken);
        indexDeadline.CancelAfter(policy.IndexBuildTimeout);
        cancellationToken = indexDeadline.Token;
        var definition = SurrealDbVectorProtocol.HnswIndex(index, table, profile.Dimensions, ConstructionEf, Neighbors);
        var timer = new ComparisonElapsedMeasurement(timeProvider);
        await SurrealDbSqlTransport.ExecuteAsync(http, definition, policy, cancellationToken: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
        await WaitAsync(http, table, index, policy, cancellationToken: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
        timer.Stop();
        using var runtimeInfo = await SurrealDbSqlTransport.QueryAsync(http, string.Format(System.Globalization.CultureInfo.InvariantCulture, INFOFORTABLEFormat, table), policy, cancellationToken: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
        var actualDefinition = SurrealDbVectorProtocol.ReadRequiredString(SurrealDbVectorProtocol.SingleResult(runtimeInfo.RootElement).GetProperty(IndexesField), index);
        if (!actualDefinition.Contains(RuntimeHnsw, StringComparison.OrdinalIgnoreCase) || !actualDefinition.Contains(RuntimeCosine, StringComparison.OrdinalIgnoreCase) || !actualDefinition.Contains(Float32, StringComparison.OrdinalIgnoreCase)
            || !HasNumericSetting(actualDefinition, DimensionSetting, profile.Dimensions)
            || !HasNumericSetting(actualDefinition, ConstructionSetting, ConstructionEf)
            || !HasNumericSetting(actualDefinition, NeighborsSetting, Neighbors))
        {
            throw new ComparisonFailureException(SurrealDbIndexDefinitionMismatch);
        }

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SurrealDbTokens.TokenAlgorithm] = SurrealDbTokens.TokenHNSW,
            [SurrealDbTokens.TokenDimension] = profile.Dimensions.ToString(CultureInfo.InvariantCulture),
            [SurrealDbTokens.TokenDistance] = SurrealDbTokens.TokenCOSINE,
            [SurrealDbTokens.TokenEfConstruction] = ConstructionEf.ToString(CultureInfo.InvariantCulture),
            [SurrealDbTokens.TokenM] = Neighbors.ToString(CultureInfo.InvariantCulture),
            [SurrealDbTokens.TokenEfSearch] = SearchEf.ToString(CultureInfo.InvariantCulture)
        };
        policy.RecordEvidence(parameters);
        return new(VectorIndexKind.Hnsw, actualDefinition, parameters, timer.Elapsed.TotalMilliseconds);
    }

    private static bool HasNumericSetting(string definition, string setting, int expected)
    {
        var tokens = definition.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (var ordinal = 0; ordinal < tokens.Length - 1; ordinal++)
        {
            if (tokens[ordinal].Equals(setting, StringComparison.OrdinalIgnoreCase))
            {
                return int.TryParse(tokens[ordinal + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var actual)
                    && actual == expected;
            }
        }
        return false;
    }

    private static async Task WaitAsync(HttpClient http, string table, string index, ComparisonExecutionOptions policy, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var started = timeProvider.GetTimestamp();
        while (true)
        {
            using var response = await SurrealDbSqlTransport.QueryAsync(http, string.Format(System.Globalization.CultureInfo.InvariantCulture, INFOFORINDEXONTABLEFormat, index, table), policy, cancellationToken: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
            var info = SurrealDbVectorProtocol.SingleResult(response.RootElement);
            if (!info.TryGetProperty(SurrealDbTokens.TokenBuilding, out var building))
            {
                throw new InvalidDataException(SurrealDbTokens.TokenSurrealDbInvalidIndexStatus);
            }

            var state = SurrealDbVectorProtocol.ReadRequiredString(building, SurrealDbTokens.TokenStatus);
            if (state.Equals(ReadyState, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (state is FailedState or AbortedState or ErrorState || timeProvider.GetElapsedTime(started) >= policy.IndexBuildTimeout)
            {
                throw new InvalidDataException(SurrealDbTokens.TokenSurrealDbHnswBuildDidNotBecomeReady);
            }

            await Task.Delay(policy.IndexPollInterval, timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }
}
