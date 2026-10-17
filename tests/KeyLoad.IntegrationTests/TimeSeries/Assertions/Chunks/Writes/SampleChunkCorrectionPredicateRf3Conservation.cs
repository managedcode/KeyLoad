using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkCorrectionPredicateRf3Conservation
{
    internal static async Task RequireAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers creator,
        SampleChunkCorrectionPredicateRf3State state, long generation, long revision, CancellationToken token)
    {
        var window = state.Window;
        var request = window.Request with
        {
            MinimumToken = state.Receipt.Token
        };
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await creator.Sdk.ReadSampleChunkWindowAsync(request, token));
        await SampleChunkRetentionRf3ReadOracle.EqualAsync(sdk, state.Expected(generation, revision, sdk.CutPosition, state.Rows));
        var official = (await McpCallerAssertions.SuccessAsync<SampleChunkWindowResult>(
            await creator.Mcp.CallAsync(SampleChunkProtocol.ReadTool, request, token))).Value;
        await SampleChunkRetentionRf3ReadOracle.EqualAsync(official, state.Expected(generation, revision, official.CutPosition, state.Rows));
        await SampleChunkRetentionRf3ReadOracle.RawAsync(creator.Sdk, creator.Mcp,
            new(window.Native.Partition, TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series, window.From, window.Until,
                SampleChunkCorrectionPredicateRf3State.CompleteResults), state.Rows, token);
        var latest = new ReadLatestSampleRequest(window.Native.Partition, TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series);
        await BothAsync(creator, McpCallerTools.SeriesLatest, latest, new LatestSampleResult(state.Rows[^1]),
            async () => await McpCallerAssertions.SdkSuccessAsync(await creator.Sdk.ReadLatestSampleAsync(latest, token)), token);
        var aggregate = new AggregateSamplesRequest(window.Native.Partition, TimeSeriesRf3Scenario.Set,
            TimeSeriesRf3Scenario.Series, window.From, window.Until);
        await BothAsync(creator, McpCallerTools.SeriesAggregate, aggregate, new SampleAggregate(3L, 6d, -0d, 4d, 2d),
            async () => await McpCallerAssertions.SdkSuccessAsync(await creator.Sdk.AggregateSamplesAsync(aggregate, token)), token);
        var windows = new AggregateSampleWindowsRequest(window.Native.Partition, TimeSeriesRf3Scenario.Set,
            TimeSeriesRf3Scenario.Series, window.From, window.Until, window.Until - window.From,
            SampleChunkCorrectionPredicateRf3State.CompleteResults, SampleChunkCorrectionPredicateRf3State.OneResult);
        await BothAsync(creator, McpCallerTools.SeriesWindows, windows,
            new SampleAggregateWindowsResult([new(window.From, window.Until, new(3L, 6d, -0d, 4d, 2d))]),
            async () => await McpCallerAssertions.SdkSuccessAsync(await creator.Sdk.AggregateSampleWindowsAsync(windows, token)), token);
        var floor = new ReadSampleRetentionRequest(window.Native.Partition, TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series);
        await BothAsync(creator, TimeSeriesReadProtocol.RetentionTool, floor, new SampleRetentionStatus(null, 0L, false),
            async () => await McpCallerAssertions.SdkSuccessAsync(await creator.Sdk.ReadSampleRetentionAsync(floor, token)), token);
        var outbox = new GetOutboxStatusRequest(window.Native.Partition);
        var baseline = state.Outbox ?? throw new InvalidOperationException(SampleChunkJobRevocationProtocol.Missing);
        var expectedCount = generation == 1L ? 4L : 5L;
        await Assert.That(baseline.Head.Tail).IsEqualTo(expectedCount);
        await Assert.That(baseline.Head.FirstAvailable).IsEqualTo(1L);
        await Assert.That(baseline.Head.StoredRecords).IsEqualTo(expectedCount);
        await Assert.That(baseline.Head.StoredBytes).IsGreaterThan(0L);
        await Assert.That(baseline.Consumers).IsEmpty();
        await BothAsync(administrator, McpCallerTools.OutboxStatus, outbox, baseline,
            async () => await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(window.Native.Partition, token)), token);
    }

    private static async Task BothAsync<T>(RequestCqrsRf3Callers callers, string tool, object request, T expected,
        Func<Task<T>> read, CancellationToken token)
    {
        await SampleChunkRetentionRf3ReadOracle.EqualAsync(await read(), expected);
        await SampleChunkRetentionRf3ReadOracle.EqualAsync((await McpCallerAssertions.SuccessAsync<T>(
            await callers.Mcp.CallAsync(tool, request, token))).Value, expected);
    }
}
