using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class ChunkMultiblockRf3Current
{
    private const int Limit = 258;
    private const long MaintenanceStep = 1;

    internal static async Task RequireAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers reader,
        ChunkMultiblockRf3State state, CancellationToken token)
    {
        var scenario = state.Scenario;
        await ChunkMultiblockRf3Pages.RequireAsync(state, reader.Sdk, reader.Mcp, token);
        var latest = new ReadLatestSampleRequest(scenario.Native.Partition, TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series);
        var rows = ChunkMultiblockRf3Expected.Visible(state.Expired, state.Healthy, state.Opened);
        await BothAsync(reader, McpCallerTools.SeriesLatest, latest, new LatestSampleResult(state.Opened ? null : rows[^1]), ct => reader.Sdk.ReadLatestSampleAsync(latest, ct), token);
        var aggregate = new AggregateSamplesRequest(scenario.Native.Partition, TimeSeriesRf3Scenario.Set,
            TimeSeriesRf3Scenario.Series, scenario.From, scenario.Until, Limit);
        await BothAsync(reader, McpCallerTools.SeriesAggregate, aggregate, ChunkMultiblockRf3Expected.Aggregate(state.Expired, state.Healthy, state.Opened),
            ct => reader.Sdk.AggregateSamplesAsync(aggregate, ct), token);
        var windows = new AggregateSampleWindowsRequest(scenario.Native.Partition, TimeSeriesRf3Scenario.Set,
            TimeSeriesRf3Scenario.Series, scenario.From, scenario.From.AddTicks(3), TimeSeriesRf3Scenario.OneTick, Limit, 3);
        await BothAsync(reader, McpCallerTools.SeriesWindows, windows,
            ChunkMultiblockRf3Expected.Windows(state.Expired, state.Healthy, state.Opened), ct => reader.Sdk.AggregateSampleWindowsAsync(windows, ct), token);
        var floor = new ReadSampleRetentionRequest(scenario.Native.Partition, TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series);
        await BothAsync(reader, McpCallerTools.SeriesRetention, floor,
            new SampleRetentionStatus(state.Expired ? scenario.From.AddTicks(1) : null, state.Purged, state.HasMore),
            ct => reader.Sdk.ReadSampleRetentionAsync(floor, ct), token);
        var outbox = new GetOutboxStatusRequest(scenario.Native.Partition);
        await OutboxAsync(state, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.OutboxStatusAsync(scenario.Native.Partition, token)));
        await OutboxAsync(state, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await administrator.Mcp.CallAsync(McpCallerTools.OutboxStatus, outbox, token))).Value);
    }

    private static async Task OutboxAsync(ChunkMultiblockRf3State state, OutboxStatus actual)
    {
        var expected = state.Outbox!;
        var pending = state.Phase is ChunkMultiblockRf3Phase.OriginalPending or ChunkMultiblockRf3Phase.HealthyPending;
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
            await SampleChunkRetentionRf3ReadOracle.EqualAsync(actual, advanced);
            state.Outbox = advanced;
            state.MaintenanceOutboxAdvanced = true;
        }
        else
        { await SampleChunkRetentionRf3ReadOracle.EqualAsync(actual, expected); }
    }

    private static async Task BothAsync<T>(RequestCqrsRf3Callers caller, string tool, object request,
        T expected, Func<CancellationToken, Task<ManagedCode.Communication.Result<T>>> read, CancellationToken token)
    {
        await SampleChunkRetentionRf3ReadOracle.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await read(token)), expected);
        await SampleChunkRetentionRf3ReadOracle.EqualAsync((await McpCallerAssertions.SuccessAsync<T>(await caller.Mcp.CallAsync(tool, request, token))).Value, expected);
    }
}
