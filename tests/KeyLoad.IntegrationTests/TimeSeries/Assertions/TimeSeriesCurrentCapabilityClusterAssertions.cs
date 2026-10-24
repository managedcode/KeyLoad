using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.TimeSeries;

internal static class TimeSeriesCurrentCapabilityClusterAssertions
{
    internal static SampleRecord[] Expected(bool fresh, string tags)
    {
        SampleRecord[] original =
            [new(TimeSeriesClusterScenario.Series, TimeSeriesClusterScenario.Data(TimeSeriesCurrentCapabilityClusterSeed.First,
                TimeSeriesClusterScenario.Start, 3d), 1L, tags),
             new(TimeSeriesClusterScenario.Series, TimeSeriesClusterScenario.Data(TimeSeriesCurrentCapabilityClusterSeed.Second,
                TimeSeriesClusterScenario.Start.AddTicks(1), 7d), 2L, tags),
             new(TimeSeriesClusterScenario.Series, TimeSeriesClusterScenario.Data(TimeSeriesCurrentCapabilityClusterSeed.Third,
                TimeSeriesClusterScenario.Start.AddTicks(2), 5d), 3L, tags)];
        return fresh ? [.. original, new(TimeSeriesClusterScenario.Series,
            TimeSeriesClusterScenario.Data(TimeSeriesCurrentCapabilityClusterSeed.Fourth,
                TimeSeriesClusterScenario.Start.AddTicks(3), 11d), 4L, tags)] : original;
    }

    internal static async Task HealthyAsync(RequestCqrsClusterCallers reader,
        TimeSeriesCurrentCapabilityClusterState state, CancellationToken token)
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
}
