using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.Comparisons;

/// <summary>Selects exactly one engine, native member count and workload on an isolated runner.</summary>
/// <param name="Target">The canonical engine name.</param>
/// <param name="NodeCount">The required actual native member count.</param>
/// <param name="Scenario">The one measured workload.</param>
/// <param name="Profile">The canonical control or scaled profile.</param>
public sealed record ComparisonWorkerSelection(string Target, int NodeCount, Scenario Scenario, string Profile)
{
    /// <summary>Exact target configuration key.</summary>
    public const string TargetSetting = "Benchmarks:Target";
    /// <summary>Actual native member-count configuration key.</summary>
    public const string NodeCountSetting = "Benchmarks:NodeCount";
    /// <summary>Single measured workload configuration key.</summary>
    public const string ScenarioSetting = "Benchmarks:Scenario";
    /// <summary>Authenticated profile configuration key.</summary>
    public const string ProfileSetting = "Benchmarks:EvidenceProfile";
    /// <summary>Optional exact scaled-profile configuration key.</summary>
    public const string ScaleProfileSetting = "Benchmarks:ScaleProfile";
    private const string AppHostProfileSetting = "Benchmarks:Profile";
    private const string GeneralAppHostProfile = "general";
    private const string TimeSeriesAppHostProfile = "timeseries";
    internal const string InvalidSelection = "IsolatedComparisonSelectionInvalid";
    private static readonly string[] WorkloadOverrideNames =
        ["Documents", "Operations", "Warmup", "Repetitions", "Concurrency", "PayloadBytes", "Seed", "Dimensions",
            "TopK", "TimeoutSeconds", "GraphVertices", "GraphFanOut", "GraphDepth"];

    /// <summary>Gets the exact typed scaled profile when the isolated worker selects one.</summary>
    public ScaledComparisonProfile? ScaledProfile { get; init; }

    /// <summary>Gets the legacy control options with the selected native topology.</summary>
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
            || !Enum.IsDefined(Scenario)
            || (ScaledProfile is not null && Scenario is not (Scenario.PointRead or Scenario.DocumentWrite
                or Scenario.DocumentUpdate or Scenario.DocumentDelete))
            || (ScaledProfile is null && Profile != contract.Profile)
            || (ScaledProfile is not null && Profile != ScaledProfile.Id))
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
        var scaleText = configuration[ScaleProfileSetting];
        var scaledProfile = scaleText is null ? null : ParseScaledProfile(scaleText);
        if (scaledProfile is not null)
        {
            ValidateScaledMode(configuration, scaledProfile);
        }
        var result = new ComparisonWorkerSelection(configuration[TargetSetting] ?? string.Empty, nodes, scenario,
            configuration[ProfileSetting] ?? string.Empty) { ScaledProfile = scaledProfile };
        result.Validate();
        return result;
    }

    private static ScaledComparisonProfile ParseScaledProfile(string id)
    {
        try
        {
            return ScaledComparisonProfileParser.Parse(id);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new InvalidOperationException(InvalidSelection);
        }
    }

    private static void ValidateScaledMode(IConfiguration configuration, ScaledComparisonProfile profile)
    {
        var appHostProfile = configuration[AppHostProfileSetting];
        if (configuration[ProfileSetting] != profile.Id
            || (appHostProfile is not null && !string.Equals(appHostProfile, GeneralAppHostProfile, StringComparison.OrdinalIgnoreCase))
            || string.Equals(appHostProfile, TimeSeriesAppHostProfile, StringComparison.OrdinalIgnoreCase)
            || HasWorkloadOverride(configuration))
        {
            throw new InvalidOperationException(InvalidSelection);
        }
    }

    private static bool HasWorkloadOverride(IConfiguration configuration)
    {
        foreach (var name in WorkloadOverrideNames)
        {
            if (configuration["Benchmarks:" + name] is not null)
            {
                return true;
            }
        }
        return false;
    }
}
