using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveSelectionTests
{
    private const string Route = "Benchmarks:Profile";
    private const string Prefix = "Benchmarks:TimeSeries:";
    private const string Target = Prefix + "Target";
    private const string Nodes = Prefix + "NodeCount";
    private const string Phase = Prefix + "Phase";
    private const string Scenario = Prefix + "Scenario";
    private const string Profile = Prefix + "EvidenceProfile";
    private const string ExpectedRoute = "timeseries-intensive";
    private const string ExpectedProfile = "intensive-timeseries-4096-c16";

    /// <summary>AC-TSI-001: four preflights and twenty intensive cells have distinct closed selections.</summary>
    [Test]
    [Arguments("KeyLoad", "1")]
    [Arguments("KeyLoad", "3")]
    [Arguments("TimescaleDB", "1")]
    [Arguments("TimescaleDB", "3")]
    public async Task AllPhysicalSelectionsRetainOnlyTheirSelectedScenario(string target, string nodes)
    {
        var values = Baseline();
        values[Target] = target;
        values[Nodes] = nodes;
        var preflight = Read(values);
        await Assert.That(preflight.Target.ToString()).IsEqualTo(target);
        await Assert.That(preflight.NodeCount.ToString(System.Globalization.CultureInfo.InvariantCulture)).IsEqualTo(nodes);
        await Assert.That(preflight.Phase).IsEqualTo(TimeSeriesIntensiveCellPhase.Preflight);
        await Assert.That(preflight.Scenario).IsNull();
        await Assert.That(preflight.EvidenceProfile).IsEqualTo(ExpectedProfile);
        values[Phase] = "Intensive";
        foreach (var scenario in new[] { "Append", "RawRangeRead", "Latest", "Aggregate", "Windows" })
        {
            values[Scenario] = scenario;
            var intensive = Read(values);
            await Assert.That(intensive.Target).IsEqualTo(preflight.Target);
            await Assert.That(intensive.NodeCount).IsEqualTo(preflight.NodeCount);
            await Assert.That(intensive.Phase).IsEqualTo(TimeSeriesIntensiveCellPhase.Intensive);
            await Assert.That(intensive.Scenario?.ToString()).IsEqualTo(scenario);
        }
    }

    [Test]
    [Arguments(Route, null)]
    [Arguments(Route, "timeseries")]
    [Arguments(Target, null)]
    [Arguments(Target, "keyload")]
    [Arguments(Target, "0")]
    [Arguments(Target, " KeyLoad")]
    [Arguments(Nodes, null)]
    [Arguments(Nodes, "")]
    [Arguments(Nodes, "0")]
    [Arguments(Nodes, "2")]
    [Arguments(Nodes, "4")]
    [Arguments(Nodes, "01")]
    [Arguments(Nodes, "+1")]
    [Arguments(Nodes, "1 ")]
    [Arguments(Phase, null)]
    [Arguments(Phase, "preflight")]
    [Arguments(Phase, "0")]
    [Arguments(Profile, null)]
    [Arguments(Profile, "intensive-512-c16")]
    [Arguments(Scenario, "")]
    [Arguments(Scenario, "Latest")]
    [Arguments("Benchmarks:Target", "KeyLoad")]
    [Arguments("Benchmarks:NodeCount", "1")]
    [Arguments("Benchmarks:Scenario", "PointRead")]
    public async Task InvalidOrMixedPreflightInputsRejectBeforeAllocation(string key, string? value)
    {
        var values = Baseline();
        values[key] = value;
        await Assert.That(() => Read(values)).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("0")]
    [Arguments("latest")]
    [Arguments("Latest ")]
    [Arguments("PointRead")]
    public async Task IntensiveRequiresAnExactDeclaredScenario(string? scenario)
    {
        var values = Baseline();
        values[Phase] = "Intensive";
        values[Scenario] = scenario;
        await Assert.That(() => Read(values)).Throws<InvalidOperationException>();
    }

    private static Dictionary<string, string?> Baseline() => new(StringComparer.Ordinal)
    {
        [Route] = ExpectedRoute,
        [Target] = "KeyLoad",
        [Nodes] = "1",
        [Phase] = "Preflight",
        [Profile] = ExpectedProfile
    };

    private static TimeSeriesIntensiveSelection Read(Dictionary<string, string?> values)
        => TimeSeriesIntensiveSelection.Read(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
}
