using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class TimeSeriesAggregateScanBoundClusterAssertions
{
    internal static SampleRecord[] Expected(bool fresh, string tags)
    {
        SampleRecord[] original =
            [new(TimeSeriesClusterScenario.Series, TimeSeriesClusterScenario.Data(TimeSeriesAggregateScanBoundClusterSeed.First,
                TimeSeriesClusterScenario.Start, 3d), 1L, tags),
             new(TimeSeriesClusterScenario.Series, TimeSeriesClusterScenario.Data(TimeSeriesAggregateScanBoundClusterSeed.Second,
                TimeSeriesClusterScenario.Start.AddTicks(1), 7d), 2L, tags),
             new(TimeSeriesClusterScenario.Series, TimeSeriesClusterScenario.Data(TimeSeriesAggregateScanBoundClusterSeed.Third,
                TimeSeriesClusterScenario.Start.AddTicks(2), 5d), 3L, tags)];
        return fresh ? [.. original, new(TimeSeriesClusterScenario.Series,
            TimeSeriesClusterScenario.Data(TimeSeriesAggregateScanBoundClusterSeed.Fourth,
                TimeSeriesClusterScenario.Start.AddTicks(3), 11d), 4L, tags)] : original;
    }

    internal static async Task HealthyAsync(RequestCqrsClusterCallers reader,
        TimeSeriesAggregateScanBoundClusterState state, CancellationToken token)
    {
        var expected = Expected(state.FreshCommand is not null, TimeSeriesClusterScenario.ProjectedTags);
        await SqlClusterProtocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadSamplesAsync(state.Range(), token)));
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<SampleRecord[]>(
            await reader.Mcp.CallAsync(McpCallerTools.SeriesRead, state.Range(), token))).Value);
        var latest = new LatestSampleResult(expected[^1]);
        await SqlClusterProtocol.EqualAsync(latest,
            await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadLatestSampleAsync(state.Scenario.Latest(), token)));
        await SqlClusterProtocol.EqualAsync(latest, (await McpCallerAssertions.SuccessAsync<LatestSampleResult>(
            await reader.Mcp.CallAsync(McpCallerTools.SeriesLatest, state.Scenario.Latest(), token))).Value);
        var aggregate = state.FreshCommand is null ? new SampleAggregate(3L, 15d, 3d, 7d, 5d)
            : new SampleAggregate(4L, 26d, 3d, 11d, 6.5d);
        await SqlClusterProtocol.EqualAsync(aggregate,
            await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.AggregateSamplesAsync(state.Aggregate(), token)));
        await SqlClusterProtocol.EqualAsync(aggregate, (await McpCallerAssertions.SuccessAsync<SampleAggregate>(
            await reader.Mcp.CallAsync(McpCallerTools.SeriesAggregate, state.Aggregate(), token))).Value);
    }

    internal static async Task CurrentAsync(RequestCqrsClusterCallers reader,
        RequestCqrsClusterCallers administrator, TimeSeriesAggregateScanBoundClusterState state,
        OutboxStatus baseline, CancellationToken token)
    {
        await HealthyAsync(reader, state, token);
        var expected = Expected(state.FreshCommand is not null, TimeSeriesClusterScenario.PrivateTags);
        await SqlClusterProtocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadSamplesAsync(state.Range(), token)));
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<SampleRecord[]>(
            await administrator.Mcp.CallAsync(McpCallerTools.SeriesRead, state.Range(), token))).Value);
        await SqlClusterProtocol.EqualAsync(baseline, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token)));
        await SqlClusterProtocol.EqualAsync(baseline, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await administrator.Mcp.CallAsync(McpCallerTools.OutboxStatus,
                new GetOutboxStatusRequest(state.Scenario.Partition), token))).Value);
    }
}
