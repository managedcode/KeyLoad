using System.Globalization;
using Microsoft.Extensions.Configuration;
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

    /// <summary>Gets the canonical document workload selected on this isolated native topology.</summary>
    public DocumentComparisonSelection? DocumentWorkload { get; init; }

    /// <summary>Gets the exact offered rate for an independently selected open-loop workload.</summary>
    public int? OpenLoopRate { get; init; }

    /// <summary>Gets the legacy control options with the selected native topology.</summary>
    public ComparisonOptions Options => CreateExecutionOptions().Value;

    [ConfigurationBinding]
    private ComparisonOptions CreateSelectedSnapshot()
    {
        Validate();
        return IsolatedComparisonContract.Current.Options with { Topology = ComparisonTopologies.FromNodeCount(NodeCount) };
    }

    /// <summary>Creates validated native execution options from the authenticated immutable manifest.</summary>
    /// <returns>The snapshot supplied to one complete comparison operation.</returns>
    [ConfigurationBinding]
    public IOptions<ComparisonOptions> CreateExecutionOptions()
    {
        var options = new OptionsManager<ComparisonOptions>(new SelectedWorkloadFactory(this));
        _ = options.Value;
        return options;
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
            || (DocumentWorkload is not null && (ScaledProfile is not null || VectorProfile is not null
                || OpenLoopRate is not null || Scenario != Scenario.PointRead
                || Profile != DocumentWorkerSelection.ProfileId(DocumentWorkload)))
            || (ScaledProfile is null && VectorProfile is null && DocumentWorkload is null && Profile != contract.Profile)
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
        return ParseSelection(ReadSelection(configuration).Value);
    }

    private static ComparisonWorkerSelection ParseSelection(ComparisonWorkerSelectionOptions selection)
    {
        var text = selection.Scenario;
        if (!int.TryParse(selection.NodeCount, NumberStyles.None, CultureInfo.InvariantCulture, out var nodes)
            || !Enum.TryParse<Scenario>(text, out var scenario) || !Enum.IsDefined(scenario) || text != scenario.ToString())
        {
            throw new InvalidOperationException(InvalidSelection);
        }
        var documents = DocumentWorkerSelection.Read(selection);
        if (documents is not null && (selection.ScaleProfile is not null || selection.VectorProfile is not null
            || selection.OpenLoopRate is not null || HasWorkloadOverride(selection)
            || selection.Profile is not null && !string.Equals(selection.Profile, GeneralAppHostProfile, StringComparison.OrdinalIgnoreCase)))
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
            DocumentWorkload = documents,
            ScaledProfile = scaledProfile,
            VectorProfile = vectorProfile,
            OpenLoopRate = OpenLoopRateSelection.Read(selection.OpenLoopRate)
        };
        result.Validate();
        return result;
    }

    [ConfigurationBinding]
    private static OptionsManager<ComparisonWorkerSelectionOptions> ReadSelection(IConfiguration configuration)
    {
        var options = new OptionsManager<ComparisonWorkerSelectionOptions>(new OptionsFactory<ComparisonWorkerSelectionOptions>(
            [new ConfigureFromConfigurationOptions<ComparisonWorkerSelectionOptions>(
                configuration.GetSection(ComparisonWorkerSelectionOptions.SectionName))], [],
            [new ValidateOptions<ComparisonWorkerSelectionOptions>(Microsoft.Extensions.Options.Options.DefaultName,
                settings => { _ = ParseSelection(settings); return true; }, InvalidSelection)]));
        _ = options.Value;
        return options;
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

    [ConfigurationBinding]
    private sealed class SelectedWorkloadFactory(ComparisonWorkerSelection selection) : IOptionsFactory<ComparisonOptions>
    {
        public ComparisonOptions Create(string name)
        {
            var snapshot = selection.CreateSelectedSnapshot();
            snapshot.Validate();
            return snapshot;
        }
    }

}
