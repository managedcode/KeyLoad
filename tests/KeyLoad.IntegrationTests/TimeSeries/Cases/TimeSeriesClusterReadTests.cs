using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

/// <summary>AC-SERIES-008..011: real SDK and official MCP reads share persisted state and authority.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class TimeSeriesClusterReadTests(ClusterFixture fixture)
{
    private const string EarlySampleId = "early";
    private const string FirstTieSampleId = "tie-first";
    private const string LastTieSampleId = "tie-last";
    private const string LaterSampleId = "later";
    private const string BelowBoundarySampleId = "below";
    private const string FirstSampleId = "first";
    private const string FirstEqualTimestampSampleId = "same-time-a";
    private const string SecondEqualTimestampSampleId = "same-time-b";
    private const string EndExclusiveSampleId = "end-exclusive";
    private const string LateSampleId = "late";
    private const string MaximumSampleId = "max";
    private const string MinimumSampleId = "min";
    [Test]
    public async Task AcSeries008LatestUsesTimestampThenSequenceAndReturnsTypedAbsence()
    {
        using var deadline = McpCallerDeadline.Create();
        var earlier = TimeSeriesClusterScenario.Start.AddMinutes(-1);
        var tie = TimeSeriesClusterScenario.Start;
        var later = TimeSeriesClusterScenario.Start.AddMinutes(1);
        var original = TimeSeriesClusterScenario.Samples(
            TimeSeriesClusterScenario.Data(EarlySampleId, earlier, -3),
            TimeSeriesClusterScenario.Data(FirstTieSampleId, tie, 4),
            TimeSeriesClusterScenario.Data(LastTieSampleId, tie.ToOffset(TimeSpan.FromHours(2)), 6),
            TimeSeriesClusterScenario.Data(LaterSampleId, later, 12));
        await SeriesUtcTiePublicTrial.RunAsync(fixture, original, deadline.Token);
    }

    [Test]
    public async Task AcSeries009AggregateMatchesIndependentRawOracleForOffsetsLateSamplesAndDeduplication()
    {
        using var deadline = McpCallerDeadline.Create();
        var start = TimeSeriesClusterScenario.Start;
        var end = start.AddMinutes(2);
        var initial = TimeSeriesClusterScenario.Samples(
            TimeSeriesClusterScenario.Data(BelowBoundarySampleId, start.AddTicks(-1), 99),
            TimeSeriesClusterScenario.Data(FirstSampleId, start, -8),
            TimeSeriesClusterScenario.Data(FirstEqualTimestampSampleId, start.AddMinutes(1), 5),
            TimeSeriesClusterScenario.Data(SecondEqualTimestampSampleId, start.AddMinutes(1).ToOffset(TimeSpan.FromHours(3)), 5),
            TimeSeriesClusterScenario.Data(EndExclusiveSampleId, end, 700));
        var late = TimeSeriesClusterScenario.Samples(TimeSeriesClusterScenario.Data(LateSampleId, start.AddSeconds(30), -2),
            TimeSeriesClusterScenario.Data(FirstSampleId, start, -8));
        var extrema = TimeSeriesClusterScenario.Samples(
            TimeSeriesClusterScenario.Data(MaximumSampleId, DateTimeOffset.MaxValue, 3),
            TimeSeriesClusterScenario.Data(MinimumSampleId, DateTimeOffset.MinValue, -5));
        await SeriesExtremeAggregateFlow.RunAsync(fixture, initial, late, extrema,
            ReadAggregateParityAsync, deadline.Token);
    }

    private static async Task<SampleAggregate> ReadAggregateParityAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        AggregateSamplesRequest request, SampleAggregate expected, Func<Task> current, CancellationToken cancellationToken)
    {
        SampleAggregate? actual = null;
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.AggregateSamplesAsync(request, cancellationToken));
            await AssertAggregateAsync(actual, expected);
            await Assert.That(actual).IsEqualTo(expected);
        }, failures);
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(current, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            var execution = await McpCallerAssertions.SuccessAsync<SampleAggregate>(
                await mcp.CallAsync(McpCallerTools.SeriesAggregate, request, cancellationToken));
            await AssertAggregateAsync(execution.Value, expected);
            await Assert.That(execution.Value).IsEqualTo(expected);
            await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(execution.Value))).IsTrue();
        }, failures);
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(current, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        return actual!;
    }

    private static async Task AssertAggregateAsync(SampleAggregate actual, SampleAggregate expected)
    {
        await Assert.That(actual.Count).IsEqualTo(expected.Count);
        await Assert.That(Math.Abs(actual.Sum - expected.Sum)).IsLessThanOrEqualTo(TimeSeriesClusterScenario.SumTolerance);
        await Assert.That(actual.Minimum).IsEqualTo(expected.Minimum);
        await Assert.That(actual.Maximum).IsEqualTo(expected.Maximum);
        if (expected.Average is null)
        {
            await Assert.That(actual.Average).IsNull();
        }
        else
        {
            await Assert.That(Math.Abs(actual.Average!.Value - expected.Average.Value))
                .IsLessThanOrEqualTo(TimeSeriesClusterScenario.SumTolerance);
        }
    }
}
