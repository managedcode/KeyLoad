using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.Comparisons;

/// <summary>Selects exactly one engine, native member count and workload on an isolated runner.</summary>
/// <param name="Target">The canonical engine name.</param>
/// <param name="NodeCount">The required actual native member count.</param>
/// <param name="Scenario">The one measured workload.</param>
/// <param name="Profile">The canonical intensive profile.</param>
public sealed record ComparisonWorkerSelection(string Target, int NodeCount, Scenario Scenario, string Profile)
{
    /// <summary>Exact target configuration key.</summary>
    public const string TargetSetting = "Benchmarks:Target";
    /// <summary>Actual native member-count configuration key.</summary>
    public const string NodeCountSetting = "Benchmarks:NodeCount";
    /// <summary>Single measured workload configuration key.</summary>
    public const string ScenarioSetting = "Benchmarks:Scenario";
    /// <summary>Canonical intensive profile configuration key.</summary>
    public const string ProfileSetting = "Benchmarks:EvidenceProfile";
    private const string InvalidSelection = "IsolatedComparisonSelectionInvalid";

    /// <summary>Gets common intensive options with the actual native topology selected.</summary>
    public ComparisonOptions Options
    {
        get
        {
            Validate();
            return IsolatedComparisonContract.Current.Options with { Topology = ComparisonTopologies.FromNodeCount(NodeCount) };
        }
    }

    /// <summary>Validates the closed engine/node/scenario/profile inventory before allocating resources.</summary>
    public void Validate()
    {
        var contract = IsolatedComparisonContract.Current;
        if (!contract.Targets.Contains(Target, StringComparer.Ordinal) || !contract.NodeCounts.Contains(NodeCount)
            || !Enum.IsDefined(Scenario) || Profile != contract.Profile)
        {
            throw new InvalidOperationException(InvalidSelection);
        }
    }

    /// <summary>Reads a strict single-cell selection from trusted workflow configuration.</summary>
    /// <param name="configuration">The trusted composition configuration.</param>
    /// <returns>The validated selection.</returns>
    public static ComparisonWorkerSelection Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var text = configuration[ScenarioSetting];
        if (!int.TryParse(configuration[NodeCountSetting], NumberStyles.None, CultureInfo.InvariantCulture, out var nodes)
            || !Enum.TryParse<Scenario>(text, out var scenario) || !Enum.IsDefined(scenario) || text != scenario.ToString())
        {
            throw new InvalidOperationException(InvalidSelection);
        }
        var result = new ComparisonWorkerSelection(configuration[TargetSetting] ?? string.Empty, nodes, scenario,
            configuration[ProfileSetting] ?? string.Empty);
        result.Validate();
        return result;
    }
}
