using System.Globalization;
using Microsoft.Extensions.Configuration;

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
        if (configuration[RouteSetting] != Route || configuration[ComparisonWorkerSelection.TargetSetting] is not null
            || configuration[ComparisonWorkerSelection.NodeCountSetting] is not null
            || configuration[ComparisonWorkerSelection.ScenarioSetting] is not null
            || !int.TryParse(configuration[NodeCountSetting], NumberStyles.None, CultureInfo.InvariantCulture, out var nodes)
            || configuration[NodeCountSetting] != nodes.ToString(CultureInfo.InvariantCulture))
        {
            throw new InvalidOperationException(Invalid);
        }
        var target = ReadEnum<TimeSeriesIntensiveTargetKind>(configuration[TargetSetting]);
        var phase = ReadEnum<TimeSeriesIntensiveCellPhase>(configuration[PhaseSetting]);
        var scenarioText = configuration[ScenarioSetting];
        TimeSeriesIntensiveScenario? scenario = scenarioText is null ? null : ReadEnum<TimeSeriesIntensiveScenario>(scenarioText);
        var result = new TimeSeriesIntensiveSelection(target, nodes, phase, scenario,
            configuration[EvidenceProfileSetting] ?? string.Empty);
        result.Validate();
        return result;
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
