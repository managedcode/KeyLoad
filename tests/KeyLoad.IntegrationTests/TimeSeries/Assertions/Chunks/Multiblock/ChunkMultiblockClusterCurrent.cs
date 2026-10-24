using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.TimeSeries;

internal static class ChunkMultiblockClusterCurrent
{
    private const int Limit = 258;
    private const long MaintenanceStep = 1;

    internal static async Task RequireAsync(RequestCqrsClusterCallers administrator, RequestCqrsClusterCallers reader,
        ChunkMultiblockClusterState state, CancellationToken token)
    {
        var scenario = state.Scenario;
        await ChunkMultiblockClusterPages.RequireAsync(state, reader.Sdk, reader.Mcp, token);
        var latest = new ReadLatestSampleRequest(scenario.Execution.Partition, TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series);
        var rows = ChunkMultiblockClusterExpected.Visible(state.Expired, state.Healthy, state.Opened);
        await BothAsync(reader, McpCallerTools.SeriesLatest, latest, new LatestSampleResult(state.Opened ? null : rows[^1]), ct => reader.Sdk.ReadLatestSampleAsync(latest, ct), token);
        var aggregate = new AggregateSamplesRequest(scenario.Execution.Partition, TimeSeriesClusterScenario.Set,
            TimeSeriesClusterScenario.Series, scenario.From, scenario.Until, Limit);
        await BothAsync(reader, McpCallerTools.SeriesAggregate, aggregate, ChunkMultiblockClusterExpected.Aggregate(state.Expired, state.Healthy, state.Opened),
            ct => reader.Sdk.AggregateSamplesAsync(aggregate, ct), token);
        var windows = new AggregateSampleWindowsRequest(scenario.Execution.Partition, TimeSeriesClusterScenario.Set,
            TimeSeriesClusterScenario.Series, scenario.From, scenario.From.AddTicks(3), TimeSeriesClusterScenario.OneTick, Limit, 3);
        await BothAsync(reader, McpCallerTools.SeriesWindows, windows,
            ChunkMultiblockClusterExpected.Windows(state.Expired, state.Healthy, state.Opened), ct => reader.Sdk.AggregateSampleWindowsAsync(windows, ct), token);
        var floor = new ReadSampleRetentionRequest(scenario.Execution.Partition, TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series);
        await BothAsync(reader, McpCallerTools.SeriesRetention, floor,
            new SampleRetentionStatus(state.Expired ? scenario.From.AddTicks(1) : null, state.Purged, state.HasMore),
            ct => reader.Sdk.ReadSampleRetentionAsync(floor, ct), token);
        var outbox = new GetOutboxStatusRequest(scenario.Execution.Partition);
        await OutboxAsync(state, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.OutboxStatusAsync(scenario.Execution.Partition, token)));
        await OutboxAsync(state, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await administrator.Mcp.CallAsync(McpCallerTools.OutboxStatus, outbox, token))).Value);
    }

    private static async Task OutboxAsync(ChunkMultiblockClusterState state, OutboxStatus actual)
    {
        var expected = state.Outbox!;
        var pending = state.Phase is ChunkMultiblockClusterPhase.OriginalPending or ChunkMultiblockClusterPhase.HealthyPending;
        if (pending && !state.MaintenanceOutboxAdvanced && actual.Head.Tail == checked(expected.Head.Tail + MaintenanceStep))
        {
            await Assert.That(actual.Head.StoredBytes).IsGreaterThan(expected.Head.StoredBytes);
            var advanced = expected with
            {
                Head = expected.Head with
                {
                    Tail = checked(expected.Head.Tail + MaintenanceStep),
                    StoredRecords = checked(expected.Head.StoredRecords + MaintenanceStep),
                    StoredBytes = actual.Head.StoredBytes
                }
            };
            await SampleChunkRetentionClusterReadOracle.EqualAsync(actual, advanced);
            state.Outbox = advanced;
            state.MaintenanceOutboxAdvanced = true;
        }
        else
        {
            await SampleChunkRetentionClusterReadOracle.EqualAsync(actual, expected);
        }
    }

    private static async Task BothAsync<T>(RequestCqrsClusterCallers caller, string tool, object request,
        T expected, Func<CancellationToken, Task<ManagedCode.Communication.Result<T>>> read, CancellationToken token)
    {
        await SampleChunkRetentionClusterReadOracle.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await read(token)), expected);
        await SampleChunkRetentionClusterReadOracle.EqualAsync((await McpCallerAssertions.SuccessAsync<T>(await caller.Mcp.CallAsync(tool, request, token))).Value, expected);
    }
}
