using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
    /// <summary>Optional exact vector-profile setting.</summary>
    public const string VectorProfileSetting = "Benchmarks:VectorProfile";
    /// <summary>Optional fixed offered-rate selector for the independent open-loop cohort.</summary>
    public const string OpenLoopRateSetting = "Benchmarks:OpenLoopRate";
    private const string GeneralAppHostProfile = "general";
    private const string TimeSeriesAppHostProfile = "timeseries";
    internal const string InvalidSelection = "IsolatedComparisonSelectionInvalid";
    /// <summary>Gets the exact typed scaled profile when the isolated worker selects one.</summary>
    public ScaledComparisonProfile? ScaledProfile { get; init; }

    /// <summary>Gets the exact vector profile selected by the isolated worker.</summary>
    public VectorComparisonProfile? VectorProfile { get; init; }

    /// <summary>Gets the exact offered rate for an independently selected open-loop workload.</summary>
    public int? OpenLoopRate { get; init; }

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
            || (ScaledProfile is not null && VectorProfile is not null)
            || (ScaledProfile is not null && Scenario is not (Scenario.PointRead or Scenario.DocumentWrite
                or Scenario.DocumentUpdate or Scenario.DocumentDelete))
            || (VectorProfile is not null && (Scenario != Scenario.VectorExact || Profile != VectorProfile.Id))
            || (ScaledProfile is null && VectorProfile is null && Profile != contract.Profile)
            || (OpenLoopRate is { } rate && (ScaledProfile is null || !OpenLoopRateSelection.IsSupported(rate)))
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
        var selection = ReadSelection(configuration).Value;
        var text = selection.Scenario;
        if (!int.TryParse(selection.NodeCount, NumberStyles.None, CultureInfo.InvariantCulture, out var nodes)
            || !Enum.TryParse<Scenario>(text, out var scenario) || !Enum.IsDefined(scenario) || text != scenario.ToString())
        {
            throw new InvalidOperationException(InvalidSelection);
        }
        var scaleText = selection.ScaleProfile;
        var scaledProfile = scaleText is null ? null : ParseScaledProfile(scaleText);
        var vectorText = selection.VectorProfile;
        var vectorProfile = vectorText is null ? null : ParseVectorProfile(vectorText);
        if (scaledProfile is not null)
        {
            ValidateScaledMode(selection, scaledProfile);
        }
        if (vectorProfile is not null)
        {
            ValidateVectorMode(selection, vectorProfile);
        }
        var result = new ComparisonWorkerSelection(selection.Target ?? string.Empty, nodes, scenario,
            selection.EvidenceProfile ?? string.Empty)
        {
            ScaledProfile = scaledProfile,
            VectorProfile = vectorProfile,
            OpenLoopRate = OpenLoopRateSelection.Read(selection.OpenLoopRate)
        };
        result.Validate();
        return result;
    }

    [ConfigurationBinding]
    private static IOptions<ComparisonWorkerSelectionOptions> ReadSelection(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddOptions<ComparisonWorkerSelectionOptions>()
            .Bind(configuration.GetSection(ComparisonWorkerSelectionOptions.SectionName));
        using var provider = services.BuildServiceProvider();
        return Microsoft.Extensions.Options.Options.Create(
            provider.GetRequiredService<IOptions<ComparisonWorkerSelectionOptions>>().Value);
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

    private static VectorComparisonProfile ParseVectorProfile(string id)
    {
        try
        { return VectorComparisonProfile.Parse(id); }
        catch (ArgumentOutOfRangeException) { throw new InvalidOperationException(InvalidSelection); }
    }

    private static void ValidateScaledMode(ComparisonWorkerSelectionOptions selection, ScaledComparisonProfile profile)
    {
        var appHostProfile = selection.Profile;
        if (selection.EvidenceProfile != profile.Id
            || selection.VectorProfile is not null
            || (appHostProfile is not null && !string.Equals(appHostProfile, GeneralAppHostProfile, StringComparison.OrdinalIgnoreCase))
            || string.Equals(appHostProfile, TimeSeriesAppHostProfile, StringComparison.OrdinalIgnoreCase)
            || HasWorkloadOverride(selection))
        {
            throw new InvalidOperationException(InvalidSelection);
        }
    }

    private static void ValidateVectorMode(ComparisonWorkerSelectionOptions selection, VectorComparisonProfile profile)
    {
        var appHostProfile = selection.Profile;
        if (selection.EvidenceProfile != profile.Id || selection.ScaleProfile is not null
            || (appHostProfile is not null && !string.Equals(appHostProfile, GeneralAppHostProfile, StringComparison.OrdinalIgnoreCase))
            || string.Equals(appHostProfile, TimeSeriesAppHostProfile, StringComparison.OrdinalIgnoreCase)
            || HasWorkloadOverride(selection))
        {
            throw new InvalidOperationException(InvalidSelection);
        }
    }

    private static bool HasWorkloadOverride(ComparisonWorkerSelectionOptions selection)
        => selection.Documents is not null || selection.Operations is not null || selection.Warmup is not null
            || selection.Repetitions is not null || selection.Concurrency is not null || selection.PayloadBytes is not null
            || selection.Seed is not null || selection.Dimensions is not null || selection.TopK is not null
            || selection.TimeoutSeconds is not null || selection.GraphVertices is not null || selection.GraphFanOut is not null
            || selection.GraphDepth is not null;
}
