using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class TimeSeriesAggregateScanBoundRf3Assertions
{
    internal static SampleRecord[] Expected(bool fresh, string tags)
    {
        SampleRecord[] original =
            [new(TimeSeriesRf3Scenario.Series, TimeSeriesRf3Scenario.Data(TimeSeriesAggregateScanBoundRf3Seed.First,
                TimeSeriesRf3Scenario.Start, 3d), 1L, tags),
             new(TimeSeriesRf3Scenario.Series, TimeSeriesRf3Scenario.Data(TimeSeriesAggregateScanBoundRf3Seed.Second,
                TimeSeriesRf3Scenario.Start.AddTicks(1), 7d), 2L, tags),
             new(TimeSeriesRf3Scenario.Series, TimeSeriesRf3Scenario.Data(TimeSeriesAggregateScanBoundRf3Seed.Third,
                TimeSeriesRf3Scenario.Start.AddTicks(2), 5d), 3L, tags)];
        return fresh ? [.. original, new(TimeSeriesRf3Scenario.Series,
            TimeSeriesRf3Scenario.Data(TimeSeriesAggregateScanBoundRf3Seed.Fourth,
                TimeSeriesRf3Scenario.Start.AddTicks(3), 11d), 4L, tags)] : original;
    }

    internal static async Task HealthyAsync(RequestCqrsRf3Callers reader,
        TimeSeriesAggregateScanBoundRf3State state, CancellationToken token)
    {
        var expected = Expected(state.FreshCommand is not null, TimeSeriesRf3Scenario.ProjectedTags);
        await SqlRf3Protocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadSamplesAsync(state.Range(), token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<SampleRecord[]>(
            await reader.Mcp.CallAsync(McpCallerTools.SeriesRead, state.Range(), token))).Value);
        var latest = new LatestSampleResult(expected[^1]);
        await SqlRf3Protocol.EqualAsync(latest,
            await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadLatestSampleAsync(state.Scenario.Latest(), token)));
        await SqlRf3Protocol.EqualAsync(latest, (await McpCallerAssertions.SuccessAsync<LatestSampleResult>(
            await reader.Mcp.CallAsync(McpCallerTools.SeriesLatest, state.Scenario.Latest(), token))).Value);
        var aggregate = state.FreshCommand is null ? new SampleAggregate(3L, 15d, 3d, 7d, 5d)
            : new SampleAggregate(4L, 26d, 3d, 11d, 6.5d);
        await SqlRf3Protocol.EqualAsync(aggregate,
            await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.AggregateSamplesAsync(state.Aggregate(), token)));
        await SqlRf3Protocol.EqualAsync(aggregate, (await McpCallerAssertions.SuccessAsync<SampleAggregate>(
            await reader.Mcp.CallAsync(McpCallerTools.SeriesAggregate, state.Aggregate(), token))).Value);
    }

    internal static async Task CurrentAsync(RequestCqrsRf3Callers reader,
        RequestCqrsRf3Callers administrator, TimeSeriesAggregateScanBoundRf3State state,
        OutboxStatus baseline, CancellationToken token)
    {
        await HealthyAsync(reader, state, token);
        var expected = Expected(state.FreshCommand is not null, TimeSeriesRf3Scenario.PrivateTags);
        await SqlRf3Protocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadSamplesAsync(state.Range(), token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<SampleRecord[]>(
            await administrator.Mcp.CallAsync(McpCallerTools.SeriesRead, state.Range(), token))).Value);
        await SqlRf3Protocol.EqualAsync(baseline, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token)));
        await SqlRf3Protocol.EqualAsync(baseline, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await administrator.Mcp.CallAsync(McpCallerTools.OutboxStatus,
                new GetOutboxStatusRequest(state.Scenario.Partition), token))).Value);
    }
}
