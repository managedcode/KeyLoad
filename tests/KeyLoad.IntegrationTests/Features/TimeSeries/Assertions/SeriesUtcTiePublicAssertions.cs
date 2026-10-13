using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SeriesUtcTiePublicAssertions
{
    internal static SampleRecord[] Rows(SeriesUtcTiePublicState state, string tags)
    {
        var rows = new List<SampleRecord>();
        if (!state.Purged)
        { rows.Add(new(TimeSeriesRf3Scenario.Series, state.Original[0], 1L, tags)); }
        rows.Add(new(TimeSeriesRf3Scenario.Series, state.Original[1], 2L, tags));
        rows.Add(new(TimeSeriesRf3Scenario.Series, state.Original[2], 3L, tags));
        if (state.Fresh)
        { rows.Add(new(TimeSeriesRf3Scenario.Series, SeriesUtcTiePublicSeed.FreshSample(), 5L, tags)); }
        rows.Add(new(TimeSeriesRf3Scenario.Series, state.Original[3], 4L, tags));
        return rows.ToArray();
    }

    internal static async Task RequireAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers reader,
        SeriesUtcTiePublicState state, CancellationToken token)
    {
        var range = new ReadSamplesRequest(state.Scenario.Partition, TimeSeriesRf3Scenario.Set,
            TimeSeriesRf3Scenario.Series, TimeSeriesRf3Scenario.Start.AddMinutes(-1), TimeSeriesRf3Scenario.Start.AddMinutes(2), 8);
        await SeriesUtcTiePublicObservation.ReadAsync(administrator, reader, state, McpCallerTools.SeriesRead,
            range, Rows(state, TimeSeriesRf3Scenario.ProjectedTags), ct => reader.Sdk.ReadSamplesAsync(range, ct), token);
        await SeriesUtcTiePublicObservation.ReadAsync(administrator, administrator, state, McpCallerTools.SeriesRead,
            range, Rows(state, TimeSeriesRf3Scenario.PrivateTags), ct => administrator.Sdk.ReadSamplesAsync(range, ct), token);
        await LatestAsync(administrator, reader, state, token);
        var aggregate = state.Fresh ? new SampleAggregate(4L, 30d, 4d, 12d, 7.5d)
            : state.Purged ? new SampleAggregate(3L, 22d, 4d, 12d, 22d / 3d) : new SampleAggregate(4L, 19d, -3d, 12d, 4.75d);
        var request = state.Scenario.Aggregate(TimeSeriesRf3Scenario.Start.AddMinutes(-1), TimeSeriesRf3Scenario.Start.AddMinutes(2), 8);
        await SeriesUtcTiePublicObservation.ReadAsync(administrator, reader, state, McpCallerTools.SeriesAggregate,
            request, aggregate, ct => reader.Sdk.AggregateSamplesAsync(request, ct), token);
        var retention = new ReadSampleRetentionRequest(state.Scenario.Partition, TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series);
        var expected = new SampleRetentionStatus(state.Purged ? TimeSeriesRf3Scenario.Start : null, state.Purged ? 1L : 0L, false);
        await SeriesUtcTiePublicObservation.ReadAsync(administrator, reader, state, McpCallerTools.SeriesRetention,
            retention, expected, ct => reader.Sdk.ReadSampleRetentionAsync(retention, ct), token);
    }

    private static async Task LatestAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers reader,
        SeriesUtcTiePublicState state, CancellationToken token)
    {
        var request = state.Scenario.Latest(atOrBefore: TimeSeriesRf3Scenario.Start.ToOffset(TimeSpan.FromHours(-5)));
        var sample = state.Fresh ? SeriesUtcTiePublicSeed.FreshSample() : state.Original[2];
        var expected = new LatestSampleResult(new(TimeSeriesRf3Scenario.Series, sample,
            state.Fresh ? 5L : 3L, TimeSeriesRf3Scenario.ProjectedTags));
        await SeriesUtcTiePublicObservation.ReadAsync(administrator, reader, state, McpCallerTools.SeriesLatest,
            request, expected, ct => reader.Sdk.ReadLatestSampleAsync(request, ct), token);
        var absent = state.Scenario.Latest(TimeSeriesRf3Scenario.EmptySeries);
        await SeriesUtcTiePublicObservation.ReadAsync(administrator, reader, state, McpCallerTools.SeriesLatest,
            absent, new LatestSampleResult(null), ct => reader.Sdk.ReadLatestSampleAsync(absent, ct), token);
        if (state.Purged)
        {
            var below = state.Scenario.Latest(atOrBefore: TimeSeriesRf3Scenario.Start.AddTicks(-1));
            await SeriesUtcTiePublicObservation.ReadAsync(administrator, reader, state, McpCallerTools.SeriesLatest,
                below, new LatestSampleResult(null), ct => reader.Sdk.ReadLatestSampleAsync(below, ct), token);
        }
    }

    internal static async Task OriginalAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, SeriesUtcTiePublicState state, CancellationToken token)
    {
        var discovered = await reader.Mcp.Client.DiscoverKeyLoadToolAsync(McpCallerTools.SeriesLatest, token);
        await McpDiscoveryAssertions.VerifyAsync(discovered);
        var request = state.Scenario.Latest(atOrBefore: TimeSeriesRf3Scenario.Start.ToOffset(TimeSpan.FromHours(-5)));
        var latest = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadLatestSampleAsync(request, token));
        await SeriesUtcTiePublicObservation.OutboxAsync(administrator, state, token);
        var native = await McpCallerAssertions.SuccessAsync<LatestSampleResult>(await reader.Mcp.CallAsync(McpCallerTools.SeriesLatest, request, token));
        await SeriesUtcTiePublicObservation.OutboxAsync(administrator, state, token);
        await Assert.That(latest.Sample?.Sample.EventId).IsEqualTo(state.Original[2].EventId);
        await Assert.That(latest.Sample?.Sequence).IsEqualTo(TimeSeriesRf3Scenario.ThirdSequence);
        await Assert.That(latest.Sample?.TagsJson).IsEqualTo(TimeSeriesRf3Scenario.ProjectedTags);
        await Assert.That(JsonDefaults.Serialize(latest).AsSpan().SequenceEqual(JsonDefaults.Serialize(native.Value))).IsTrue();
        var absent = state.Scenario.Latest(TimeSeriesRf3Scenario.EmptySeries);
        await Assert.That((await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadLatestSampleAsync(absent, token))).Sample).IsNull();
        await SeriesUtcTiePublicObservation.OutboxAsync(administrator, state, token);
        var absentMcp = await McpCallerAssertions.SuccessAsync<LatestSampleResult>(await reader.Mcp.CallAsync(McpCallerTools.SeriesLatest, absent, token));
        await Assert.That(absentMcp.Value.Sample).IsNull();
        await SeriesUtcTiePublicObservation.OutboxAsync(administrator, state, token);
    }
}
