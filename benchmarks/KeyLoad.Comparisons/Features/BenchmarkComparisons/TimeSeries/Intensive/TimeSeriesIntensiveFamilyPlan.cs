using System.Collections.Immutable;
using System.Globalization;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveFamilyPlan
{
    private const string PreflightSuffix = "preflight";
    private const string KeyLoadId = "keyload";
    private const string TimescaleDbId = "timescaledb";
    private const string IdPrefix = "ts-";
    private const string NodeCountSegmentPrefix = "-n";
    private const string SuffixSegmentPrefix = "-";

    internal static TimeSeriesIntensiveFamilyCells Create(TimeSeriesIntensiveFamilyContract contract)
    {
        TimeSeriesIntensiveFamilyContractValidation.Validate(contract);
        var preflight = ImmutableArray.CreateBuilder<TimeSeriesIntensiveFamilyCell>(contract.ExpectedPreflightCells);
        var intensive = ImmutableArray.CreateBuilder<TimeSeriesIntensiveFamilyCell>(contract.ExpectedIntensiveCells);
        foreach (var target in contract.Targets)
        {
            var targetId = TargetId(target);
            foreach (var nodeCount in contract.NodeCounts)
            {
                Add(preflight, target, nodeCount, TimeSeriesIntensiveCellPhase.Preflight, null,
                    CellId(targetId, nodeCount, PreflightSuffix));
                foreach (var scenario in contract.Scenarios)
                {
                    Add(intensive, target, nodeCount, TimeSeriesIntensiveCellPhase.Intensive, scenario,
                        CellId(targetId, nodeCount, scenario.ToString()));
                }
            }
        }
        return new TimeSeriesIntensiveFamilyCells(preflight.MoveToImmutable(), intensive.MoveToImmutable());
    }

    private static void Add(ImmutableArray<TimeSeriesIntensiveFamilyCell>.Builder cells,
        TimeSeriesIntensiveTargetKind target, int nodeCount, TimeSeriesIntensiveCellPhase phase,
        TimeSeriesIntensiveScenario? scenario, string id)
    {
        var selection = new TimeSeriesIntensiveSelection(target, nodeCount, phase, scenario,
            TimeSeriesIntensiveProfile.Name);
        selection.Validate();
        cells.Add(new TimeSeriesIntensiveFamilyCell(id, selection));
    }

    private static string CellId(string target, int nodeCount, string suffix)
        => string.Concat(IdPrefix, target, NodeCountSegmentPrefix,
            nodeCount.ToString(CultureInfo.InvariantCulture), SuffixSegmentPrefix, suffix);

    private static string TargetId(TimeSeriesIntensiveTargetKind target)
        => target switch
        {
            TimeSeriesIntensiveTargetKind.KeyLoad => KeyLoadId,
            TimeSeriesIntensiveTargetKind.TimescaleDB => TimescaleDbId,
            _ => throw new InvalidDataException(TimeSeriesIntensiveFamilyContractErrors.TargetInvalid),
        };
}
