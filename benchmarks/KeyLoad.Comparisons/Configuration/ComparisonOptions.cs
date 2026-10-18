using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

/// <summary>Configures the generated workload and its measurement budgets.</summary>
[ConfigurationOptions]
public sealed record ComparisonOptions : IComparisonSettings
{
    private const int MinimumCount = 1;
    private const int MinimumWarmup = 0;
    private const int MaximumDocuments = 1_000_000;
    private const int MaximumOperations = 1_000_000;
    private const int MaximumWarmup = 100_000;
    private const int MaximumRepetitions = 20;
    private const int MaximumConcurrency = 128;
    private const int MinimumPayloadBytes = 128;
    private const int MaximumPayloadBytes = 65_536;
    private const int MinimumDimensions = 2;
    private const int MaximumDimensions = 1_024;
    private const int MaximumTopK = 100;
    private const int MaximumTimeoutSeconds = 120;
    private const int MaximumGraphVertices = 512;
    private const int MaximumGraphFanOut = 8;
    private const int MaximumGraphDepth = 5;
    /// <summary>The actual workload configuration section.</summary>
    public const string SectionName = "Benchmarks";
    /// <summary>Preserved workload rejection text.</summary>
    public const string ValidationMessage = "The benchmark configuration exceeds its budgets.";
    /// <summary>Gets or initializes the requested target topology.</summary>
    public ComparisonTopology Topology { get; init; } = ComparisonTopology.Standalone;
    /// <summary>Gets or initializes the deterministic corpus and request-selection seed.</summary>
    public int Seed { get; init; } = 1729;
    /// <summary>Gets or initializes the number of documents in the shared corpus.</summary>
    public int Documents { get; init; } = 1_000;
    /// <summary>Gets or initializes the number of measured operations in each repetition.</summary>
    public int Operations { get; init; } = 1_000;
    /// <summary>Gets or initializes the number of unmeasured warmup operations per repetition.</summary>
    public int Warmup { get; init; } = 50;
    /// <summary>Gets or initializes the number of measured repetitions.</summary>
    public int Repetitions { get; init; } = 3;
    /// <summary>Gets or initializes the maximum number of concurrent workers.</summary>
    public int Concurrency { get; init; } = 8;
    /// <summary>Gets or initializes the target serialized document payload size in bytes.</summary>
    public int PayloadBytes { get; init; } = 1_024;
    /// <summary>Gets or initializes the number of dimensions in each generated vector.</summary>
    public int Dimensions { get; init; } = 32;
    /// <summary>Gets or initializes the number of results retained by exact-neighbor workloads.</summary>
    public int TopK { get; init; } = 10;
    /// <summary>Gets or initializes the per-operation timeout budget in seconds.</summary>
    public int TimeoutSeconds { get; init; } = 30;
    /// <summary>Gets or initializes the maximum number of generated graph vertices.</summary>
    public int GraphVertices { get; init; } = 256;
    /// <summary>Gets or initializes the generated outgoing edge count per graph vertex, subject to component size.</summary>
    public int GraphFanOut { get; init; } = 3;
    /// <summary>Gets or initializes the maximum graph traversal depth.</summary>
    public int GraphDepth { get; init; } = 3;

    /// <summary>Reads and validates benchmark settings from the <c>Benchmarks</c> configuration section.</summary>
    /// <param name="configuration">The configuration source containing benchmark settings.</param>
    /// <returns>The validated native snapshot, retaining canonical defaults for omitted configuration.</returns>
    public static IOptions<ComparisonOptions> Read(IConfiguration configuration)
        => ComparisonOptionsRegistration.Read(configuration);

    /// <summary>Checks each configured option against its supported individual range.</summary>
    public void Validate()
    {
        if (!IsValid())
        {
            throw new ArgumentOutOfRangeException(nameof(ComparisonOptions), ValidationMessage);
        }
    }
    /// <summary>Checks the unchanged admitted workload ranges before native execution.</summary>
    /// <returns>Whether every configured workload budget is supported.</returns>
    public bool IsValid() => Enum.IsDefined(Topology)
        && Documents is >= MinimumCount and <= MaximumDocuments
        && Operations is >= MinimumCount and <= MaximumOperations
        && Warmup is >= MinimumWarmup and <= MaximumWarmup
        && Repetitions is >= MinimumCount and <= MaximumRepetitions
        && Concurrency is >= MinimumCount and <= MaximumConcurrency
        && PayloadBytes is >= MinimumPayloadBytes and <= MaximumPayloadBytes
        && Dimensions is >= MinimumDimensions and <= MaximumDimensions
        && TopK >= MinimumCount && TopK <= Math.Min(Documents, MaximumTopK)
        && TimeoutSeconds is >= MinimumCount and <= MaximumTimeoutSeconds
        && GraphVertices is >= MinimumCount and <= MaximumGraphVertices
        && GraphFanOut is >= MinimumCount and <= MaximumGraphFanOut
        && GraphDepth is >= MinimumCount and <= MaximumGraphDepth;
}
