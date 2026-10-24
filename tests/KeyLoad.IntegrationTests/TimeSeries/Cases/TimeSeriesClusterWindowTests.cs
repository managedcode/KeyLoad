using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.TimeSeries;

/// <summary>AC-SERIES-010: fixed windows are dense, UTC anchored and bounded on real Orleans.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class TimeSeriesClusterWindowTests(ClusterFixture fixture)
{
    private const string FirstWindowSampleId = "window-first";
    private const string LastWindowSampleId = "window-last";
    private const string ExcludedWindowSampleId = "window-excluded";
    private const string MaximumSampleId = "window-maximum";

    [Test]
    public async Task AcSeries010WindowsAreDenseAnchoredUtcAndClampThePartialFinalWindow()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await TimeSeriesClusterScenario.CreateAsync(fixture, deadline.Token);
        var start = TimeSeriesClusterScenario.Start;
        var end = start.AddMinutes(2).AddSeconds(30);
        var samples = TimeSeriesClusterScenario.Samples(
            TimeSeriesClusterScenario.Data(FirstWindowSampleId, start.AddSeconds(5), 2),
            TimeSeriesClusterScenario.Data(LastWindowSampleId, start.AddMinutes(2).AddSeconds(10), -4),
            TimeSeriesClusterScenario.Data(ExcludedWindowSampleId, end, 9));
        await scenario.AppendAsync(fixture, TimeSeriesClusterScenario.Series, samples,
            TimeSeriesClusterScenario.PublicTags, deadline.Token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            TimeSeriesClusterScenario.Set, Capability.SeriesRead, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, TimeSeriesClusterScenario.Node3);
        var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, TimeSeriesClusterScenario.Node2,
            identity.Secret, deadline.Token);
        var request = scenario.Windows(start.ToOffset(TimeSpan.FromHours(-6)), end.ToOffset(TimeSpan.FromHours(5)),
            TimeSeriesClusterScenario.Minute);
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.AggregateSampleWindowsAsync(request, deadline.Token));
        var execution = await McpCallerAssertions.SuccessAsync<SampleAggregateWindowsResult>(
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
        await AssertAggregateAsync(windows[0].Aggregate, TimeSeriesClusterScenario.RawOracle([samples[0]]));
        await AssertAggregateAsync(windows[1].Aggregate, TimeSeriesClusterScenario.RawOracle([]));
        await Assert.That(windows[2].UntilExclusive).IsEqualTo(end);
        await AssertAggregateAsync(windows[2].Aggregate, TimeSeriesClusterScenario.RawOracle([samples[1]]));
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(execution.Value))).IsTrue();
        await VerifyMaximumTickAsync(fixture, sdk, mcp, scenario, deadline.Token);
    }

    private static async Task VerifyMaximumTickAsync(ClusterFixture fixture, KeyLoadClient sdk,
        McpOfficialClient mcp, TimeSeriesClusterScenario scenario, CancellationToken cancellationToken)
    {
        await scenario.AppendAsync(fixture, TimeSeriesClusterScenario.WindowMaximumSeries,
            TimeSeriesClusterScenario.Samples(TimeSeriesClusterScenario.Data(MaximumSampleId, DateTimeOffset.MaxValue, 11)),
            TimeSeriesClusterScenario.PublicTags, cancellationToken);
        var request = new AggregateSampleWindowsRequest(scenario.Partition, TimeSeriesClusterScenario.Set,
            TimeSeriesClusterScenario.WindowMaximumSeries, DateTimeOffset.MaxValue, null, TimeSpan.FromTicks(1));
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.AggregateSampleWindowsAsync(request, cancellationToken));
        var execution = await McpCallerAssertions.SuccessAsync<SampleAggregateWindowsResult>(
            await mcp.CallAsync(McpCallerTools.SeriesWindows, request, cancellationToken));
        await Assert.That(actual.Windows).HasSingleItem();
        await Assert.That(actual.Windows[0].UntilExclusive).IsNull();
        await AssertAggregateAsync(actual.Windows[0].Aggregate,
            TimeSeriesClusterScenario.RawOracle([TimeSeriesClusterScenario.Data(MaximumSampleId, DateTimeOffset.MaxValue, 11)]));
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(execution.Value))).IsTrue();
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
