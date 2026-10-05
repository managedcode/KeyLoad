using System.Text.Json.Serialization;
using System.Collections.Immutable;

namespace KeyLoad.Comparisons;

/// <summary>The closed vector workload axes admitted by native qualification.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VectorIndexKind { Exact, Hnsw, IvfFlat, NativeAnn }

/// <summary>The native vector query workload applied by a vector profile.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VectorQueryMode { Plain, Filtered, Mixed }

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

    public string Id { get; }
    public int RecordCount { get; }
    public VectorIndexKind IndexKind { get; }
    public VectorQueryMode QueryMode { get; }
    public int Dimensions => 128;
    public string Metric => "Cosine";
    public int TopK => 10;
    public int Seed => 1729;
    public int PayloadBytes => 1024;
    public int QueryVectorCount => 64;
    public int WarmupQueries => 256;
    public int MeasuredQueries => 100_000;
    public int Concurrency => 16;
    public int TimeoutSeconds => 30;
    public int LatencySampleCount => 4096;
    public int Repetitions => 1;
    public double MinimumRecall => IndexKind == VectorIndexKind.Exact ? 1d : 0.95d;
    public int UpdateCount => QueryMode == VectorQueryMode.Mixed ? 10_000 : 0;

    public static VectorComparisonProfile Parse(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var parts = id.Split('-', StringSplitOptions.None);
        if (parts.Length != 5 || parts[0] != "vector" || parts[4] != "c16")
            throw new ArgumentOutOfRangeException(nameof(id), "Unknown vector comparison profile.");
        var count = parts[1] switch { "100k" => 100_000, "1m" => 1_000_000, _ => 0 };
        var kind = parts[2] switch { "exact" => VectorIndexKind.Exact, "hnsw" => VectorIndexKind.Hnsw,
            "ivfflat" => VectorIndexKind.IvfFlat, "native" => VectorIndexKind.NativeAnn, _ => (VectorIndexKind?)null };
        var mode = parts[3] switch { "plain" => VectorQueryMode.Plain, "filtered" => VectorQueryMode.Filtered,
            "mixed" => VectorQueryMode.Mixed, _ => (VectorQueryMode?)null };
        if (count == 0 || kind is null || mode is null)
            throw new ArgumentOutOfRangeException(nameof(id), "Unknown vector comparison profile.");
        return new(id, count, kind.Value, mode.Value);
    }

    public static IReadOnlyList<string> AllIds { get; } =
        (from scale in new[] { "100k", "1m" }
         from method in new[] { "exact", "hnsw", "ivfflat", "native" }
         from mode in new[] { "plain", "filtered", "mixed" }
         select $"vector-{scale}-{method}-{mode}-c16").ToArray();
}

/// <summary>Measured result and native evidence from one vector workload case.</summary>
public sealed record VectorMetrics(
    int RecordCount, int LoadedRecordCount, int QueryAttempts, int QuerySuccesses,
    int UpdateAttempts, int UpdateSuccesses, double ExactRecall, double MinimumRecall, int RecallSamples,
    ImmutableArray<double> PerQueryRecall, double LatencyP95Ms, double LatencyP99Ms, double IndexBuildMilliseconds, string IndexKind,
    string NativeIndexDefinition, string NativeQueryPlan, IReadOnlyDictionary<string, string> IndexParameters,
    long? ServerMemoryBytes, int? ServerMemorySamplingIntervalMs,
    double QueryElapsedSeconds, double QueryUsefulOperationsPerSecond,
    double UpdateElapsedSeconds, double UpdateUsefulOperationsPerSecond);
