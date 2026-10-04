using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

/// <summary>AC-SERIES-010: fixed windows are dense, UTC anchored and bounded on real RF3.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class TimeSeriesRf3WindowTests(ClusterFixture fixture)
{
    private const string FirstWindowSampleId = "window-first";
    private const string LastWindowSampleId = "window-last";
    private const string ExcludedWindowSampleId = "window-excluded";
    private const string MaximumSampleId = "window-maximum";

    [Test]
    public async Task AcSeries010WindowsAreDenseAnchoredUtcAndClampThePartialFinalWindow()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await TimeSeriesRf3Scenario.CreateAsync(fixture, deadline.Token);
        var start = TimeSeriesRf3Scenario.Start;
        var end = start.AddMinutes(2).AddSeconds(30);
        var samples = TimeSeriesRf3Scenario.Samples(
            TimeSeriesRf3Scenario.Data(FirstWindowSampleId, start.AddSeconds(5), 2),
            TimeSeriesRf3Scenario.Data(LastWindowSampleId, start.AddMinutes(2).AddSeconds(10), -4),
            TimeSeriesRf3Scenario.Data(ExcludedWindowSampleId, end, 9));
        await scenario.AppendAsync(fixture, TimeSeriesRf3Scenario.Series, samples,
            TimeSeriesRf3Scenario.PublicTags, deadline.Token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            TimeSeriesRf3Scenario.Set, Capability.SeriesRead, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, TimeSeriesRf3Scenario.Node3);
        var sdk = new KeyLoadClient(http, identity.Secret);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, TimeSeriesRf3Scenario.Node2,
            identity.Secret, deadline.Token);
        var request = scenario.Windows(start.ToOffset(TimeSpan.FromHours(-6)), end.ToOffset(TimeSpan.FromHours(5)),
            TimeSeriesRf3Scenario.Minute);
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.AggregateSampleWindowsAsync(request, deadline.Token));
        var native = await McpCallerAssertions.SuccessAsync<SampleAggregateWindowsResult>(
            await mcp.CallAsync(McpCallerTools.SeriesWindows, request, deadline.Token));
        var windows = actual.Windows;

        await Assert.That(windows.Length).IsEqualTo(3);
        await Assert.That(windows[0].From).IsEqualTo(start);
        await Assert.That(windows[0].UntilExclusive).IsEqualTo(start.AddMinutes(1));
        await Assert.That(windows[1].From).IsEqualTo(start.AddMinutes(1));
        await Assert.That(windows[1].UntilExclusive).IsEqualTo(start.AddMinutes(2));
        await Assert.That(windows[2].From).IsEqualTo(start.AddMinutes(2));
        await Assert.That(windows.All(window => window.From.Offset == TimeSpan.Zero
            && (window.UntilExclusive is null || window.UntilExclusive.Value.Offset == TimeSpan.Zero))).IsTrue();
        await AssertAggregateAsync(windows[0].Aggregate, TimeSeriesRf3Scenario.RawOracle([samples[0]]));
        await AssertAggregateAsync(windows[1].Aggregate, TimeSeriesRf3Scenario.RawOracle([]));
        await Assert.That(windows[2].UntilExclusive).IsEqualTo(end);
        await AssertAggregateAsync(windows[2].Aggregate, TimeSeriesRf3Scenario.RawOracle([samples[1]]));
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(native.Value))).IsTrue();
        await VerifyMaximumTickAsync(fixture, sdk, mcp, scenario, deadline.Token);
    }

    private static async Task VerifyMaximumTickAsync(ClusterFixture fixture, KeyLoadClient sdk,
        McpOfficialClient mcp, TimeSeriesRf3Scenario scenario, CancellationToken cancellationToken)
    {
        await scenario.AppendAsync(fixture, TimeSeriesRf3Scenario.WindowMaximumSeries,
            TimeSeriesRf3Scenario.Samples(TimeSeriesRf3Scenario.Data(MaximumSampleId, DateTimeOffset.MaxValue, 11)),
            TimeSeriesRf3Scenario.PublicTags, cancellationToken);
        var request = new AggregateSampleWindowsRequest(scenario.Partition, TimeSeriesRf3Scenario.Set,
            TimeSeriesRf3Scenario.WindowMaximumSeries, DateTimeOffset.MaxValue, null, TimeSpan.FromTicks(1));
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.AggregateSampleWindowsAsync(request, cancellationToken));
        var native = await McpCallerAssertions.SuccessAsync<SampleAggregateWindowsResult>(
            await mcp.CallAsync(McpCallerTools.SeriesWindows, request, cancellationToken));
        await Assert.That(actual.Windows).HasSingleItem();
        await Assert.That(actual.Windows[0].UntilExclusive).IsNull();
        await AssertAggregateAsync(actual.Windows[0].Aggregate,
            TimeSeriesRf3Scenario.RawOracle([TimeSeriesRf3Scenario.Data(MaximumSampleId, DateTimeOffset.MaxValue, 11)]));
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(native.Value))).IsTrue();
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
