using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace KeyLoad.Comparisons;

/// <summary>The closed vector workload axes admitted by native qualification.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VectorIndexKind
{
    /// <summary>Uses native exact vector distance ordering without an ANN index.</summary>
    Exact,
    /// <summary>Uses a Hierarchical Navigable Small World index.</summary>
    Hnsw,
    /// <summary>Uses an inverted-file flat index.</summary>
    IvfFlat,
    /// <summary>Uses a database's native ANN method without relabeling its algorithm.</summary>
    NativeAnn
}

/// <summary>The native vector query workload applied by a vector profile.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VectorQueryMode
{
    /// <summary>Searches the complete corpus without a predicate.</summary>
    Plain,
    /// <summary>Searches records whose number is divisible by 100.</summary>
    Filtered,
    /// <summary>Searches the stable 90 percent while updating excluded records.</summary>
    Mixed
}

/// <summary>One immutable vector workload profile with its corpus, query and accuracy bounds.</summary>
[JsonConverter(typeof(VectorComparisonProfileJsonConverter))]
public sealed record VectorComparisonProfile
{
    private VectorComparisonProfile(string id, int recordCount, VectorIndexKind indexKind, VectorQueryMode queryMode)
    {
        Id = id;
        RecordCount = recordCount;
        IndexKind = indexKind;
        QueryMode = queryMode;
    }

    /// <summary>Gets the exact accepted profile identifier.</summary>
    public string Id { get; }
    /// <summary>Gets the number of native corpus records.</summary>
    public int RecordCount { get; }
    /// <summary>Gets the native vector index method selected by this profile.</summary>
    public VectorIndexKind IndexKind { get; }
    /// <summary>Gets the query and mutation mode.</summary>
    public VectorQueryMode QueryMode { get; }
    /// <summary>Gets the fixed float32 vector dimension.</summary>
    public int Dimensions => VectorComparisonProfileValues.VectorDimensions;
    /// <summary>Gets the vector metric used by native search and the oracle.</summary>
    public string Metric => VectorComparisonProfileValues.Cosine;
    /// <summary>Gets the fixed number of returned neighbors.</summary>
    public int TopK => VectorComparisonProfileValues.TopKNeighbors;
    /// <summary>Gets the deterministic corpus and request seed.</summary>
    public int Seed => VectorComparisonProfileValues.CorpusSeed;
    /// <summary>Gets the exact canonical document payload length.</summary>
    public int PayloadBytes => VectorComparisonProfileValues.PayloadBytes;
    /// <summary>Gets the number of distinct deterministic query vectors.</summary>
    public int QueryVectorCount => VectorComparisonProfileValues.QueryVectorCount;
    /// <summary>Gets the unmeasured warmup request count.</summary>
    public int WarmupQueries => VectorComparisonProfileValues.WarmupQueries;
    /// <summary>Gets the measured search request count.</summary>
    public int MeasuredQueries => VectorComparisonProfileValues.Scale100kCount;
    /// <summary>Gets the closed-loop query concurrency.</summary>
    public int Concurrency => VectorComparisonProfileValues.QueryConcurrency;
    /// <summary>Gets the per-native-request timeout in seconds.</summary>
    public int TimeoutSeconds => VectorComparisonProfileValues.OperationTimeoutSeconds;
    /// <summary>Gets the number of evenly spaced latency observations retained.</summary>
    public int LatencySampleCount => VectorComparisonProfileValues.LatencySampleCount;
    /// <summary>Gets the number of repetitions.</summary>
    public int Repetitions => VectorComparisonProfileValues.RepetitionCount;
    /// <summary>Gets the minimum required aggregate recall.</summary>
    public double MinimumRecall => IndexKind == VectorIndexKind.Exact ? VectorComparisonProfileValues.ExactRecall : VectorComparisonProfileValues.ApproximateRecallTarget;
    /// <summary>Gets the deterministic update count for mixed query profiles.</summary>
    public int UpdateCount => QueryMode == VectorQueryMode.Mixed ? VectorComparisonProfileValues.MixedUpdateCount : VectorComparisonProfileValues.EmptyCount;

    /// <summary>Parses one member of the closed vector-profile inventory.</summary>
    /// <param name="id">The canonical profile ID.</param>
    /// <returns>The fixed profile settings for the ID.</returns>
    public static VectorComparisonProfile Parse(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var parts = id.Split(VectorComparisonProfileValues.ProfileDelimiterCharacter, StringSplitOptions.None);
        if (parts.Length != VectorComparisonProfileValues.ProfilePartCount || parts[VectorComparisonProfileValues.FamilyPartIndex] != VectorProfileTokens.Family || parts[VectorComparisonProfileValues.ConcurrencyPartIndex] != VectorProfileTokens.ConcurrencySuffix)
        {
            throw new ArgumentOutOfRangeException(nameof(id), VectorComparisonProfileValues.UnknownVectorComparisonProfile);
        }

        var count = parts[VectorComparisonProfileValues.ScalePartIndex] switch { VectorProfileTokens.Scale100k => VectorComparisonProfileValues.Scale100kCount, VectorProfileTokens.Scale1m => VectorComparisonProfileValues.Scale1mCount, _ => VectorComparisonProfileValues.EmptyCount };
        var kind = parts[VectorComparisonProfileValues.MethodPartIndex] switch
        {
            VectorProfileTokens.ExactMethod => VectorIndexKind.Exact,
            VectorProfileTokens.HnswMethod => VectorIndexKind.Hnsw,
            VectorProfileTokens.IvfFlatMethod => VectorIndexKind.IvfFlat,
            VectorProfileTokens.NativeMethod => VectorIndexKind.NativeAnn,
            _ => (VectorIndexKind?)null
        };
        var mode = parts[VectorComparisonProfileValues.ModePartIndex] switch
        {
            VectorProfileTokens.PlainMode => VectorQueryMode.Plain,
            VectorProfileTokens.FilteredMode => VectorQueryMode.Filtered,
            VectorProfileTokens.MixedMode => VectorQueryMode.Mixed,
            _ => (VectorQueryMode?)null
        };
        if (count == VectorComparisonProfileValues.EmptyCount || kind is null || mode is null)
        {
            throw new ArgumentOutOfRangeException(nameof(id), VectorComparisonProfileValues.UnknownVectorComparisonProfile);
        }

        return new(id, count, kind.Value, mode.Value);
    }

    /// <summary>Gets all 24 active profile IDs in stable scale, index, and mode order.</summary>
    public static IReadOnlyList<string> AllIds { get; } =
        (from scale in new[] { VectorProfileTokens.Scale100k, VectorProfileTokens.Scale1m }
         from method in new[] { VectorProfileTokens.ExactMethod, VectorProfileTokens.HnswMethod, VectorProfileTokens.IvfFlatMethod, VectorProfileTokens.NativeMethod }
         from mode in new[] { VectorProfileTokens.PlainMode, VectorProfileTokens.FilteredMode, VectorProfileTokens.MixedMode }
         select $"{VectorComparisonProfileValues.Vector}{scale}{VectorComparisonProfileValues.ProfileDelimiter}{method}{VectorComparisonProfileValues.ProfileDelimiter}{mode}{VectorComparisonProfileValues.C16}").ToArray();
}

/// <summary>Measured vector results, native index evidence and per-query accuracy.</summary>
/// <param name="RecordCount">The requested corpus record count.</param>
/// <param name="LoadedRecordCount">The actual native ingestion record count.</param>
/// <param name="QueryAttempts">The number of attempted measured queries.</param>
/// <param name="QuerySuccesses">The number of successfully completed measured queries.</param>
/// <param name="UpdateAttempts">The number of attempted embedding updates.</param>
/// <param name="UpdateSuccesses">The number of acknowledged and verified embedding updates.</param>
/// <param name="ExactRecall">The mean recall against the independent exact oracle.</param>
/// <param name="MinimumRecall">The lowest per-query recall.</param>
/// <param name="RecallSamples">The number of per-query recall observations.</param>
/// <param name="PerQueryRecall">Recall for each measured query.</param>
/// <param name="LatencyP95Ms">The estimated p95 query latency in milliseconds.</param>
/// <param name="LatencyP99Ms">The estimated p99 query latency in milliseconds.</param>
/// <param name="IndexBuildMilliseconds">Observed native index creation duration.</param>
/// <param name="IndexKind">The requested index kind label.</param>
/// <param name="NativeIndexDefinition">The actual native index definition.</param>
/// <param name="NativeQueryPlan">The actual native measured query plan.</param>
/// <param name="IndexParameters">Native index and search parameters.</param>
/// <param name="ServerMemoryBytes">Native server memory from the AppHost sidecar, when joined.</param>
/// <param name="ServerMemorySamplingIntervalMs">Native server memory sampling interval, when joined.</param>
/// <param name="QueryElapsedSeconds">The measured query phase duration.</param>
/// <param name="QueryUsefulOperationsPerSecond">The measured useful query rate.</param>
/// <param name="UpdateElapsedSeconds">The concurrent update phase duration.</param>
/// <param name="UpdateUsefulOperationsPerSecond">The verified update rate.</param>
public sealed record VectorMetrics(
    int RecordCount, int LoadedRecordCount, int QueryAttempts, int QuerySuccesses,
    int UpdateAttempts, int UpdateSuccesses, double ExactRecall, double MinimumRecall, int RecallSamples,
    ImmutableArray<double> PerQueryRecall, double LatencyP95Ms, double LatencyP99Ms, double IndexBuildMilliseconds, string IndexKind,
    string NativeIndexDefinition, string NativeQueryPlan, IReadOnlyDictionary<string, string> IndexParameters,
    long? ServerMemoryBytes, int? ServerMemorySamplingIntervalMs,
    double QueryElapsedSeconds, double QueryUsefulOperationsPerSecond,
    double UpdateElapsedSeconds, double UpdateUsefulOperationsPerSecond);
