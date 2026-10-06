using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyLoad.Comparisons;

/// <summary>The canonical closed workload contract embedded from the same artifact used by CI planning.</summary>
public sealed record IsolatedComparisonContract
{
    private const string ResourceName = "KeyLoad.Comparisons.IsolatedContract";
    private const string InvalidContract = "IsolatedComparisonContractInvalid";
    private static readonly Lazy<IsolatedComparisonContract> Contract = new(Read);

    /// <summary>Gets the current embedded contract.</summary>
    public static IsolatedComparisonContract Current => Contract.Value;
    /// <summary>Gets the canonical contract version.</summary>
    public int SchemaVersion { get; init; }
    /// <summary>Gets the raw per-worker envelope version.</summary>
    public int WorkerSchemaVersion { get; init; }
    /// <summary>Gets the exact intensive profile identity.</summary>
    public string Profile { get; init; } = string.Empty;
    /// <summary>Gets the complete engine inventory.</summary>
    [JsonRequired] public ImmutableArray<string> Targets { get; init; }
    /// <summary>Gets the allowed native member counts.</summary>
    [JsonRequired] public ImmutableArray<int> NodeCounts { get; init; }
    /// <summary>Gets the common document workload family.</summary>
    [JsonRequired] public ImmutableArray<Scenario> CrudScenarios { get; init; }
    /// <summary>Gets the specialized workload family.</summary>
    [JsonRequired] public ImmutableArray<Scenario> SpecializedScenarios { get; init; }
    /// <summary>Gets the common options, without a varying native topology.</summary>
    [JsonRequired, SerializedOptionsSnapshot] public ComparisonOptions Options { get; init; } = null!;
    /// <summary>Gets the explicitly unsupported Community cluster configurations.</summary>
    [JsonRequired] public ImmutableArray<UnsupportedComparisonTopology> UnsupportedTopologies { get; init; }

    private static IsolatedComparisonContract Read()
    {
        const int ComparisonSchemaVersion = 1;
        const int AcceptedWorkerSchemaVersion = 5;
        const int SingleNodeTopology = 1;
        const int TwoNodeReplicaCount = 2;
        const int ThreeNodeTopology = 3;

        using var resource = typeof(IsolatedComparisonContract).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(InvalidContract);
        var result = JsonSerializer.Deserialize<IsolatedComparisonContract>(resource, ReportWriter.JsonOptions)
            ?? throw new InvalidOperationException(InvalidContract);
        result.Options.Validate();
        if (result.SchemaVersion != ComparisonSchemaVersion || result.WorkerSchemaVersion != AcceptedWorkerSchemaVersion || string.IsNullOrWhiteSpace(result.Profile)
            || result.Targets.IsDefaultOrEmpty || result.Targets.Distinct(StringComparer.Ordinal).Count() != result.Targets.Length
            || !result.NodeCounts.SequenceEqual([SingleNodeTopology, TwoNodeReplicaCount, ThreeNodeTopology])
            || !result.CrudScenarios.Concat(result.SpecializedScenarios).Order().SequenceEqual(Enum.GetValues<Scenario>().Order()))
        {
            throw new InvalidOperationException(InvalidContract);
        }
        return result;
    }
}

/// <summary>Describes a native Community topology that cannot be measured.</summary>
/// <param name="Target">The exact engine name.</param>
/// <param name="NodeCounts">Unsupported actual member counts.</param>
/// <param name="Reason">The explicit capability or licensing reason.</param>
public sealed record UnsupportedComparisonTopology(string Target,
    [property: JsonRequired] ImmutableArray<int> NodeCounts, string Reason);
