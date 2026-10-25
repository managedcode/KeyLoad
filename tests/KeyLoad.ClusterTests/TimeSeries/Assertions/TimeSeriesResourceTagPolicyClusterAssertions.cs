using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.TimeSeries;

internal static class TimeSeriesResourceTagPolicyClusterAssertions
{
    internal static SampleRecord[] Expected(string tags) =>
        [new(TimeSeriesClusterScenario.Series, TimeSeriesClusterScenario.Data(TimeSeriesResourceTagPolicyClusterSeed.FirstId,
            TimeSeriesClusterScenario.Start, 3d), 1L, tags),
         new(TimeSeriesClusterScenario.Series, TimeSeriesClusterScenario.Data(TimeSeriesResourceTagPolicyClusterSeed.SecondId,
            TimeSeriesClusterScenario.Start.AddTicks(1), 7d), 2L, tags)];

    internal static async Task CurrentAsync(RequestCqrsClusterCallers administrator,
        TimeSeriesResourceTagPolicyClusterState state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => CanonicalAsync(administrator, state, token), failures);
        await ServerFailureObserver.ObserveAsync(() => TimeSeriesResourceChunkPolicyClusterRead.AdministratorAsync(
            administrator, state, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CanonicalAsync(RequestCqrsClusterCallers administrator,
        TimeSeriesResourceTagPolicyClusterState state, CancellationToken token)
    {
        var expected = Expected(TimeSeriesClusterScenario.PrivateTags);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await SqlClusterProtocol.EqualAsync(expected,
                await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadSamplesAsync(state.Range(), token)));
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<SampleRecord[]>(
                await administrator.Mcp.CallAsync(McpCallerTools.SeriesRead, state.Range(), token))).Value);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await SqlClusterProtocol.EqualAsync(state.Outbox,
                await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token)));
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await SqlClusterProtocol.EqualAsync(state.Outbox, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
                await administrator.Mcp.CallAsync(McpCallerTools.OutboxStatus,
                    new GetOutboxStatusRequest(state.Scenario.Partition), token))).Value);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task ReadAsync(RequestCqrsClusterCallers reader, RequestCqrsClusterCallers administrator,
        TimeSeriesResourceTagPolicyClusterState state, CancellationToken token)
    {
        var tags = state.Current.SchemaVersion == 2L ? TimeSeriesClusterScenario.ProjectedTags : TimeSeriesClusterScenario.PrivateTags;
        var expected = Expected(tags);
        await SqlClusterProtocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadSamplesAsync(state.Range(), token)));
        await CurrentAsync(administrator, state, token);
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<SampleRecord[]>(
            await reader.Mcp.CallAsync(McpCallerTools.SeriesRead, state.Range(), token))).Value);
        await CurrentAsync(administrator, state, token);
        var latest = new LatestSampleResult(expected[1]);
        await SqlClusterProtocol.EqualAsync(latest,
            await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadLatestSampleAsync(state.Scenario.Latest(), token)));
        await CurrentAsync(administrator, state, token);
        await SqlClusterProtocol.EqualAsync(latest, (await McpCallerAssertions.SuccessAsync<LatestSampleResult>(
            await reader.Mcp.CallAsync(McpCallerTools.SeriesLatest, state.Scenario.Latest(), token))).Value);
        await CurrentAsync(administrator, state, token);
        var aggregate = new SampleAggregate(2L, 10d, 3d, 7d, 5d);
        await SqlClusterProtocol.EqualAsync(aggregate,
            await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.AggregateSamplesAsync(state.Aggregate(), token)));
        await CurrentAsync(administrator, state, token);
        await SqlClusterProtocol.EqualAsync(aggregate, (await McpCallerAssertions.SuccessAsync<SampleAggregate>(
            await reader.Mcp.CallAsync(McpCallerTools.SeriesAggregate, state.Aggregate(), token))).Value);
        await CurrentAsync(administrator, state, token);
        await TimeSeriesResourceChunkPolicyClusterRead.RunAsync(reader, administrator, state, token);
    }
}
