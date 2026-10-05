using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

/// <summary>AC-SERIES-008..011: real SDK and official MCP reads share persisted state and authority.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class TimeSeriesRf3ReadTests(ClusterFixture fixture)
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
        var scenario = await TimeSeriesRf3Scenario.CreateAsync(fixture, deadline.Token);
        var earlier = TimeSeriesRf3Scenario.Start.AddMinutes(-1);
        var tie = TimeSeriesRf3Scenario.Start;
        var later = TimeSeriesRf3Scenario.Start.AddMinutes(1);
        await scenario.AppendAsync(fixture, TimeSeriesRf3Scenario.Series,
            TimeSeriesRf3Scenario.Samples(
                TimeSeriesRf3Scenario.Data(EarlySampleId, earlier, -3),
                TimeSeriesRf3Scenario.Data(FirstTieSampleId, tie, 4),
                TimeSeriesRf3Scenario.Data(LastTieSampleId, tie.ToOffset(TimeSpan.FromHours(2)), 6),
                TimeSeriesRf3Scenario.Data(LaterSampleId, later, 12)),
            TimeSeriesRf3Scenario.PrivateTags, deadline.Token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            TimeSeriesRf3Scenario.Set, Capability.SeriesRead, deadline.Token);
        var request = scenario.Latest(atOrBefore: tie.ToOffset(TimeSpan.FromHours(-5)));
        using var http = McpCallerHttp.Create(fixture, TimeSeriesRf3Scenario.Node3);
        var sdk = new KeyLoadClient(http, identity.Secret);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, TimeSeriesRf3Scenario.Node2,
            identity.Secret, deadline.Token);
        var discovered = await mcp.Client.DiscoverKeyLoadToolAsync(McpCallerTools.SeriesLatest, deadline.Token);
        await McpDiscoveryAssertions.VerifyAsync(discovered);
        var latest = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadLatestSampleAsync(request, deadline.Token));
        var native = await McpCallerAssertions.SuccessAsync<LatestSampleResult>(
            await mcp.CallAsync(McpCallerTools.SeriesLatest, request, deadline.Token));
        await Assert.That(latest.Sample?.Sample.EventId).IsEqualTo(LastTieSampleId);
        await Assert.That(latest.Sample?.Sequence).IsEqualTo(TimeSeriesRf3Scenario.ThirdSequence);
        await Assert.That(latest.Sample?.TagsJson).IsEqualTo(TimeSeriesRf3Scenario.ProjectedTags);
        await Assert.That(JsonDefaults.Serialize(latest).AsSpan().SequenceEqual(JsonDefaults.Serialize(native.Value))).IsTrue();
        var absent = scenario.Latest(TimeSeriesRf3Scenario.EmptySeries);
        await Assert.That((await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadLatestSampleAsync(absent, deadline.Token))).Sample)
            .IsNull();
        var absentMcp = await McpCallerAssertions.SuccessAsync<LatestSampleResult>(
            await mcp.CallAsync(McpCallerTools.SeriesLatest, absent, deadline.Token));
        await Assert.That(absentMcp.Value.Sample).IsNull();
    }

    [Test]
    public async Task AcSeries009AggregateMatchesIndependentRawOracleForOffsetsLateSamplesAndDeduplication()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await TimeSeriesRf3Scenario.CreateAsync(fixture, deadline.Token);
        var start = TimeSeriesRf3Scenario.Start;
        var end = start.AddMinutes(2);
        var initial = TimeSeriesRf3Scenario.Samples(
            TimeSeriesRf3Scenario.Data(BelowBoundarySampleId, start.AddTicks(-1), 99),
            TimeSeriesRf3Scenario.Data(FirstSampleId, start, -8),
            TimeSeriesRf3Scenario.Data(FirstEqualTimestampSampleId, start.AddMinutes(1), 5),
            TimeSeriesRf3Scenario.Data(SecondEqualTimestampSampleId, start.AddMinutes(1).ToOffset(TimeSpan.FromHours(3)), 5),
            TimeSeriesRf3Scenario.Data(EndExclusiveSampleId, end, 700));
        await scenario.AppendAsync(fixture, TimeSeriesRf3Scenario.Series, initial,
            TimeSeriesRf3Scenario.PublicTags, deadline.Token);
        await scenario.AppendAsync(fixture, TimeSeriesRf3Scenario.Series,
            TimeSeriesRf3Scenario.Samples(TimeSeriesRf3Scenario.Data(LateSampleId, start.AddSeconds(30), -2),
                TimeSeriesRf3Scenario.Data(FirstSampleId, start, -8)),
            TimeSeriesRf3Scenario.PublicTags, deadline.Token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            TimeSeriesRf3Scenario.Set, Capability.SeriesRead, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, TimeSeriesRf3Scenario.Node1);
        var sdk = new KeyLoadClient(http, identity.Secret);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, TimeSeriesRf3Scenario.Node2,
            identity.Secret, deadline.Token);
        var request = scenario.Aggregate(start.ToOffset(TimeSpan.FromHours(4)), end.ToOffset(TimeSpan.FromHours(-2)));
        var expected = TimeSeriesRf3Scenario.RawOracle(initial.Skip(1).Take(3)
            .Append(TimeSeriesRf3Scenario.Data(LateSampleId, start.AddSeconds(30), -2)));
        var actual = await ReadAggregateParityAsync(sdk, mcp, request, deadline.Token);
        await AssertAggregateAsync(actual, expected);
        var empty = await McpCallerAssertions.SdkSuccessAsync(await sdk.AggregateSamplesAsync(
            scenario.Aggregate(start, start), deadline.Token));
        await AssertAggregateAsync(empty, TimeSeriesRf3Scenario.RawOracle([]));
        await scenario.AppendAsync(fixture, TimeSeriesRf3Scenario.Series,
            TimeSeriesRf3Scenario.Samples(
                TimeSeriesRf3Scenario.Data(MaximumSampleId, DateTimeOffset.MaxValue, 3),
                TimeSeriesRf3Scenario.Data(MinimumSampleId, DateTimeOffset.MinValue, -5)),
            TimeSeriesRf3Scenario.PublicTags, deadline.Token);
        var throughMaximum = await ReadAggregateParityAsync(sdk, mcp,
            scenario.Aggregate(DateTimeOffset.MaxValue, null), deadline.Token);
        await AssertAggregateAsync(throughMaximum, TimeSeriesRf3Scenario.RawOracle(
            [TimeSeriesRf3Scenario.Data(MaximumSampleId, DateTimeOffset.MaxValue, 3)]));
        var throughMinimum = await ReadAggregateParityAsync(sdk, mcp,
            scenario.Aggregate(DateTimeOffset.MinValue, DateTimeOffset.MinValue.AddTicks(1)), deadline.Token);
        await AssertAggregateAsync(throughMinimum, TimeSeriesRf3Scenario.RawOracle(
            [TimeSeriesRf3Scenario.Data(MinimumSampleId, DateTimeOffset.MinValue, -5)]));
    }

    private static async Task<SampleAggregate> ReadAggregateParityAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        AggregateSamplesRequest request, CancellationToken cancellationToken)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.AggregateSamplesAsync(request, cancellationToken));
        var native = await McpCallerAssertions.SuccessAsync<SampleAggregate>(
            await mcp.CallAsync(McpCallerTools.SeriesAggregate, request, cancellationToken));
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(native.Value))).IsTrue();
        return actual;
    }

    private static async Task AssertAggregateAsync(SampleAggregate actual, SampleAggregate expected)
    {
        await Assert.That(actual.Count).IsEqualTo(expected.Count);
        await Assert.That(Math.Abs(actual.Sum - expected.Sum)).IsLessThanOrEqualTo(TimeSeriesRf3Scenario.SumTolerance);
        await Assert.That(actual.Minimum).IsEqualTo(expected.Minimum);
        await Assert.That(actual.Maximum).IsEqualTo(expected.Maximum);
        if (expected.Average is null)
        {
            await Assert.That(actual.Average).IsNull();
        }
        else
        {
            await Assert.That(Math.Abs(actual.Average!.Value - expected.Average.Value))
                .IsLessThanOrEqualTo(TimeSeriesRf3Scenario.SumTolerance);
        }
    }
}
