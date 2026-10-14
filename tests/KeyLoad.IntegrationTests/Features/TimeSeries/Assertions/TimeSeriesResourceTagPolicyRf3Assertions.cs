using KeyLoad.Client;
using KeyLoad.Server;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class TimeSeriesResourceTagPolicyRf3Assertions
{
    internal static SampleRecord[] Expected(string tags) =>
        [new(TimeSeriesRf3Scenario.Series, TimeSeriesRf3Scenario.Data(TimeSeriesResourceTagPolicyRf3Seed.FirstId,
            TimeSeriesRf3Scenario.Start, 3d), 1L, tags),
         new(TimeSeriesRf3Scenario.Series, TimeSeriesRf3Scenario.Data(TimeSeriesResourceTagPolicyRf3Seed.SecondId,
            TimeSeriesRf3Scenario.Start.AddTicks(1), 7d), 2L, tags)];

    internal static async Task CurrentAsync(RequestCqrsRf3Callers administrator,
        TimeSeriesResourceTagPolicyRf3State state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => CanonicalAsync(administrator, state, token), failures);
        await ServerFailureObserver.ObserveAsync(() => TimeSeriesResourceChunkPolicyRf3Read.AdministratorAsync(
            administrator, state, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CanonicalAsync(RequestCqrsRf3Callers administrator,
        TimeSeriesResourceTagPolicyRf3State state, CancellationToken token)
    {
        var expected = Expected(TimeSeriesRf3Scenario.PrivateTags);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await SqlRf3Protocol.EqualAsync(expected,
                await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadSamplesAsync(state.Range(), token)));
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<SampleRecord[]>(
                await administrator.Mcp.CallAsync(McpCallerTools.SeriesRead, state.Range(), token))).Value);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await SqlRf3Protocol.EqualAsync(state.Outbox,
                await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token)));
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await SqlRf3Protocol.EqualAsync(state.Outbox, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
                await administrator.Mcp.CallAsync(McpCallerTools.OutboxStatus,
                    new GetOutboxStatusRequest(state.Scenario.Partition), token))).Value);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await SqlRf3Protocol.EqualAsync(state.Placement, await McpCallerAssertions.SdkSuccessAsync(
                await administrator.Sdk.ReadAtomicPartitionPlacementAsync(new(1, state.Scenario.Partition), token)));
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await SqlRf3Protocol.EqualAsync(state.Placement, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
                await administrator.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead,
                    new AtomicPartitionPlacementReadRequest(1, state.Scenario.Partition), token))).Value);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task ReadAsync(RequestCqrsRf3Callers reader, RequestCqrsRf3Callers administrator,
        TimeSeriesResourceTagPolicyRf3State state, CancellationToken token)
    {
        var tags = state.Current.SchemaVersion == 2L ? TimeSeriesRf3Scenario.ProjectedTags : TimeSeriesRf3Scenario.PrivateTags;
        var expected = Expected(tags);
        await SqlRf3Protocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadSamplesAsync(state.Range(), token)));
        await CurrentAsync(administrator, state, token);
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<SampleRecord[]>(
            await reader.Mcp.CallAsync(McpCallerTools.SeriesRead, state.Range(), token))).Value);
        await CurrentAsync(administrator, state, token);
        var latest = new LatestSampleResult(expected[1]);
        await SqlRf3Protocol.EqualAsync(latest,
            await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadLatestSampleAsync(state.Scenario.Latest(), token)));
        await CurrentAsync(administrator, state, token);
        await SqlRf3Protocol.EqualAsync(latest, (await McpCallerAssertions.SuccessAsync<LatestSampleResult>(
            await reader.Mcp.CallAsync(McpCallerTools.SeriesLatest, state.Scenario.Latest(), token))).Value);
        await CurrentAsync(administrator, state, token);
        var aggregate = new SampleAggregate(2L, 10d, 3d, 7d, 5d);
        await SqlRf3Protocol.EqualAsync(aggregate,
            await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.AggregateSamplesAsync(state.Aggregate(), token)));
        await CurrentAsync(administrator, state, token);
        await SqlRf3Protocol.EqualAsync(aggregate, (await McpCallerAssertions.SuccessAsync<SampleAggregate>(
            await reader.Mcp.CallAsync(McpCallerTools.SeriesAggregate, state.Aggregate(), token))).Value);
        await CurrentAsync(administrator, state, token);
        await TimeSeriesResourceChunkPolicyRf3Read.RunAsync(reader, administrator, state, token);
    }
}
