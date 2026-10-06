using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed record TimeSeriesIntensiveSelection(TimeSeriesIntensiveTargetKind Target, int NodeCount,
    TimeSeriesIntensiveCellPhase Phase, TimeSeriesIntensiveScenario? Scenario, string EvidenceProfile)
{
    internal const string RouteSetting = "Benchmarks:Profile";
    internal const string Route = "timeseries-intensive";
    internal const string Prefix = "Benchmarks:TimeSeries:";
    internal const string TargetSetting = Prefix + "Target";
    internal const string NodeCountSetting = Prefix + "NodeCount";
    internal const string PhaseSetting = Prefix + "Phase";
    internal const string ScenarioSetting = Prefix + "Scenario";
    internal const string EvidenceProfileSetting = Prefix + "EvidenceProfile";
    private const string Invalid = "TimeSeriesIntensiveSelectionInvalid";
    private const int MinimumNodes = 1;
    private const int MaximumNodes = 3;

    internal void Validate()
    {
        if (!Enum.IsDefined(Target) || NodeCount is < MinimumNodes or > MaximumNodes || !Enum.IsDefined(Phase)
            || EvidenceProfile != TimeSeriesIntensiveProfile.Name
            || (Phase == TimeSeriesIntensiveCellPhase.Preflight ? Scenario is not null : Scenario is null)
            || (Scenario is { } scenario && !Enum.IsDefined(scenario)))
        {
            throw new InvalidOperationException(Invalid);
        }
    }

    internal static TimeSeriesIntensiveSelection Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return ParseSelection(ReadSelection(configuration).Value);
    }

    private static TimeSeriesIntensiveSelection ParseSelection(TimeSeriesIntensiveSelectionOptions selection)
    {
        var cell = selection.TimeSeries;
        if (selection.Profile != Route || selection.Target is not null || selection.NodeCount is not null
            || selection.Scenario is not null || cell is null
            || !int.TryParse(cell.NodeCount, NumberStyles.None, CultureInfo.InvariantCulture, out var nodes)
            || cell.NodeCount != nodes.ToString(CultureInfo.InvariantCulture))
        {
            throw new InvalidOperationException(Invalid);
        }
        var target = ReadEnum<TimeSeriesIntensiveTargetKind>(cell.Target);
        var phase = ReadEnum<TimeSeriesIntensiveCellPhase>(cell.Phase);
        var scenarioText = cell.Scenario;
        TimeSeriesIntensiveScenario? scenario = scenarioText is null ? null : ReadEnum<TimeSeriesIntensiveScenario>(scenarioText);
        var result = new TimeSeriesIntensiveSelection(target, nodes, phase, scenario,
            cell.EvidenceProfile ?? string.Empty);
        result.Validate();
        return result;
    }

    [ConfigurationBinding]
    private static OptionsManager<TimeSeriesIntensiveSelectionOptions> ReadSelection(IConfiguration configuration)
    {
        var options = new OptionsManager<TimeSeriesIntensiveSelectionOptions>(new OptionsFactory<TimeSeriesIntensiveSelectionOptions>(
            [new ConfigureFromConfigurationOptions<TimeSeriesIntensiveSelectionOptions>(
                configuration.GetSection(TimeSeriesIntensiveSelectionOptions.SectionName))], [],
            [new ValidateOptions<TimeSeriesIntensiveSelectionOptions>(Options.DefaultName,
                settings => { _ = ParseSelection(settings); return true; }, Invalid)]));
        _ = options.Value;
        return options;
    }

    private static T ReadEnum<T>(string? text) where T : struct, Enum
    {
        if (!Enum.TryParse<T>(text, out var result) || !Enum.IsDefined(result) || text != result.ToString())
        {
            throw new InvalidOperationException(Invalid);
        }
        return result;
    }
}
