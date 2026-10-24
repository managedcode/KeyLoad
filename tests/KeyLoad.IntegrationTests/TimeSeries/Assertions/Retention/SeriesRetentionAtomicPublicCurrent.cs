using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.TimeSeries;

internal static class SeriesRetentionAtomicPublicCurrent
{
    internal static async Task AssertAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, SeriesRetentionAtomicPublicState state, CancellationToken token)
    {
        var range = SeriesRetentionAtomicPublicExpected.Range(state);
        await BothAsync(reader, McpCallerTools.SeriesRead, range, SeriesRetentionAtomicPublicExpected.Rows(state),
            ct => reader.Sdk.ReadSamplesAsync(range, ct), token);
        var latest = SeriesRetentionAtomicPublicExpected.LatestRequest(state);
        await BothAsync(reader, McpCallerTools.SeriesLatest, latest, SeriesRetentionAtomicPublicExpected.Latest(state),
            ct => reader.Sdk.ReadLatestSampleAsync(latest, ct), token);
        var retention = SeriesRetentionAtomicPublicExpected.RetentionRequest(state);
        await BothAsync(reader, McpCallerTools.SeriesRetention, retention, SeriesRetentionAtomicPublicExpected.Retention(state),
            ct => reader.Sdk.ReadSampleRetentionAsync(retention, ct), token);
        var aggregate = SeriesRetentionAtomicPublicExpected.AggregateRequest(state);
        await BothAsync(reader, McpCallerTools.SeriesAggregate, aggregate, SeriesRetentionAtomicPublicExpected.Aggregate(state),
            ct => reader.Sdk.AggregateSamplesAsync(aggregate, ct), token);
        var windows = SeriesRetentionAtomicPublicExpected.WindowsRequest(state);
        await BothAsync(reader, McpCallerTools.SeriesWindows, windows, SeriesRetentionAtomicPublicExpected.Windows(state),
            ct => reader.Sdk.AggregateSampleWindowsAsync(windows, ct), token);
        await BothAsync(administrator, McpCallerTools.OutboxStatus,
            new GetOutboxStatusRequest(state.Scenario.Partition), state.CurrentOutbox,
            ct => administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, ct), token);
    }

    private static async Task BothAsync<T>(RequestCqrsClusterCallers caller, string tool, object request,
        T expected, Func<CancellationToken, Task<ManagedCode.Communication.Result<T>>> read, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await read(token)));
        await SqlClusterProtocol.EqualAsync(expected,
            (await McpCallerAssertions.SuccessAsync<T>(await caller.Mcp.CallAsync(tool, request, token))).Value);
    }
}
