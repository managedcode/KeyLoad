using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;

namespace KeyLoad.ClusterTests.TimeSeries;

internal static class SeriesUtcTiePublicAssertions
{
    internal static SampleRecord[] Rows(SeriesUtcTiePublicState state, string tags)
    {
        var rows = new List<SampleRecord>();
        if (!state.Purged)
        {
            rows.Add(new(TimeSeriesClusterScenario.Series, state.Original[0], 1L, tags));
        }
        rows.Add(new(TimeSeriesClusterScenario.Series, state.Original[1], 2L, tags));
        rows.Add(new(TimeSeriesClusterScenario.Series, state.Original[2], 3L, tags));
        if (state.Fresh)
        {
            rows.Add(new(TimeSeriesClusterScenario.Series, SeriesUtcTiePublicSeed.FreshSample(), 5L, tags));
        }
        rows.Add(new(TimeSeriesClusterScenario.Series, state.Original[3], 4L, tags));
        return rows.ToArray();
    }

    internal static async Task RequireAsync(RequestCqrsClusterCallers administrator, RequestCqrsClusterCallers reader,
        SeriesUtcTiePublicState state, CancellationToken token)
    {
        var range = new ReadSamplesRequest(state.Scenario.Partition, TimeSeriesClusterScenario.Set,
            TimeSeriesClusterScenario.Series, TimeSeriesClusterScenario.Start.AddMinutes(-1), TimeSeriesClusterScenario.Start.AddMinutes(2), 8);
        await SeriesUtcTiePublicObservation.ReadAsync(administrator, reader, state, McpCallerTools.SeriesRead,
            range, Rows(state, TimeSeriesClusterScenario.ProjectedTags), ct => reader.Sdk.ReadSamplesAsync(range, ct), token);
        await SeriesUtcTiePublicObservation.ReadAsync(administrator, administrator, state, McpCallerTools.SeriesRead,
            range, Rows(state, TimeSeriesClusterScenario.PrivateTags), ct => administrator.Sdk.ReadSamplesAsync(range, ct), token);
        await LatestAsync(administrator, reader, state, token);
        var aggregate = state.Fresh ? new SampleAggregate(4L, 30d, 4d, 12d, 7.5d)
            : state.Purged ? new SampleAggregate(3L, 22d, 4d, 12d, 22d / 3d) : new SampleAggregate(4L, 19d, -3d, 12d, 4.75d);
        var request = state.Scenario.Aggregate(TimeSeriesClusterScenario.Start.AddMinutes(-1), TimeSeriesClusterScenario.Start.AddMinutes(2), 8);
        await SeriesUtcTiePublicObservation.ReadAsync(administrator, reader, state, McpCallerTools.SeriesAggregate,
            request, aggregate, ct => reader.Sdk.AggregateSamplesAsync(request, ct), token);
        var retention = new ReadSampleRetentionRequest(state.Scenario.Partition, TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series);
        var expected = new SampleRetentionStatus(state.Purged ? TimeSeriesClusterScenario.Start : null, state.Purged ? 1L : 0L, false);
        await SeriesUtcTiePublicObservation.ReadAsync(administrator, reader, state, McpCallerTools.SeriesRetention,
            retention, expected, ct => reader.Sdk.ReadSampleRetentionAsync(retention, ct), token);
    }

    private static async Task LatestAsync(RequestCqrsClusterCallers administrator, RequestCqrsClusterCallers reader,
        SeriesUtcTiePublicState state, CancellationToken token)
    {
        var request = state.Scenario.Latest(atOrBefore: TimeSeriesClusterScenario.Start.ToOffset(TimeSpan.FromHours(-5)));
        var sample = state.Fresh ? SeriesUtcTiePublicSeed.FreshSample() : state.Original[2];
        var expected = new LatestSampleResult(new(TimeSeriesClusterScenario.Series, sample,
            state.Fresh ? 5L : 3L, TimeSeriesClusterScenario.ProjectedTags));
        await SeriesUtcTiePublicObservation.ReadAsync(administrator, reader, state, McpCallerTools.SeriesLatest,
            request, expected, ct => reader.Sdk.ReadLatestSampleAsync(request, ct), token);
        var absent = state.Scenario.Latest(TimeSeriesClusterScenario.EmptySeries);
        await SeriesUtcTiePublicObservation.ReadAsync(administrator, reader, state, McpCallerTools.SeriesLatest,
            absent, new LatestSampleResult(null), ct => reader.Sdk.ReadLatestSampleAsync(absent, ct), token);
        if (state.Purged)
        {
            var below = state.Scenario.Latest(atOrBefore: TimeSeriesClusterScenario.Start.AddTicks(-1));
            await SeriesUtcTiePublicObservation.ReadAsync(administrator, reader, state, McpCallerTools.SeriesLatest,
                below, new LatestSampleResult(null), ct => reader.Sdk.ReadLatestSampleAsync(below, ct), token);
        }
    }

    internal static async Task OriginalAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, SeriesUtcTiePublicState state, CancellationToken token)
    {
        var discovered = await reader.Mcp.Client.DiscoverKeyLoadToolAsync(McpCallerTools.SeriesLatest, token);
        await McpDiscoveryAssertions.VerifyAsync(discovered);
        var request = state.Scenario.Latest(atOrBefore: TimeSeriesClusterScenario.Start.ToOffset(TimeSpan.FromHours(-5)));
        var latest = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadLatestSampleAsync(request, token));
        await SeriesUtcTiePublicObservation.OutboxAsync(administrator, state, token);
        var execution = await McpCallerAssertions.SuccessAsync<LatestSampleResult>(await reader.Mcp.CallAsync(McpCallerTools.SeriesLatest, request, token));
        await SeriesUtcTiePublicObservation.OutboxAsync(administrator, state, token);
        await Assert.That(latest.Sample?.Sample.EventId).IsEqualTo(state.Original[2].EventId);
        await Assert.That(latest.Sample?.Sequence).IsEqualTo(TimeSeriesClusterScenario.ThirdSequence);
        await Assert.That(latest.Sample?.TagsJson).IsEqualTo(TimeSeriesClusterScenario.ProjectedTags);
        await Assert.That(JsonDefaults.Serialize(latest).AsSpan().SequenceEqual(JsonDefaults.Serialize(execution.Value))).IsTrue();
        var absent = state.Scenario.Latest(TimeSeriesClusterScenario.EmptySeries);
        await Assert.That((await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadLatestSampleAsync(absent, token))).Sample).IsNull();
        await SeriesUtcTiePublicObservation.OutboxAsync(administrator, state, token);
        var absentMcp = await McpCallerAssertions.SuccessAsync<LatestSampleResult>(await reader.Mcp.CallAsync(McpCallerTools.SeriesLatest, absent, token));
        await Assert.That(absentMcp.Value.Sample).IsNull();
        await SeriesUtcTiePublicObservation.OutboxAsync(administrator, state, token);
    }
}
