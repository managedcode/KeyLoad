using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class TimeSeriesChunkQualificationRf3Reads
{
    internal static async Task RunAsync(RequestCqrsRf3Callers callers, TimeSeriesChunkQualificationRf3State state,
        bool projected, CancellationToken token)
    {
        var failures = new List<Exception>();
        for (var index = 0; index < state.Partitions.Length; index++)
        {
            await ServerFailureObserver.ObserveAsync(() => ScopeAsync(callers, state, index, projected, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task ScopeAsync(RequestCqrsRf3Callers callers, TimeSeriesChunkQualificationRf3State state,
        int index, bool projected, CancellationToken token)
    {
        var partition = state.Partitions[index];
        var sibling = index == TimeSeriesChunkQualificationRf3Data.SiblingScope;
        var request = TimeSeriesChunkQualificationRf3Data.Request(partition, state.Window) with
        {
            MinimumToken = state.Originals[index].Receipt.Token
        };
        if (!sibling && state.Phase == TimeSeriesChunkQualificationRf3Data.DroppedPhase)
        {
            await TimeSeriesChunkQualificationRf3Refusals.ChunkAsync(callers, request, ErrorCode.HistoryUnavailable, projected ? state.Reader.Secret : state.RootCredential,
                TimeSeriesChunkQualificationRf3Data.Missing, TimeSeriesChunkQualificationRf3Data.SafeUnavailable, token);
        }
        else
        {
            await ChunkAsync(callers, state, request, sibling, projected, token);
        }
        var rows = TimeSeriesChunkQualificationRf3Data.Rows(sibling, state.Phase, projected);
        var raw = new ReadSamplesRequest(partition, TimeSeriesChunkQualificationRf3Data.Set, TimeSeriesChunkQualificationRf3Data.Series,
            TimeSeriesChunkQualificationRf3Data.From, TimeSeriesChunkQualificationRf3Data.Until, TimeSeriesChunkQualificationRf3Data.ReadLimit);
        await AllAsync(callers, partition, McpCallerTools.SeriesRead, raw, () => RawAsync(callers, raw, token), rows, token);
        var latest = new ReadLatestSampleRequest(partition, TimeSeriesChunkQualificationRf3Data.Set, TimeSeriesChunkQualificationRf3Data.Series);
        await AllAsync(callers, partition, TimeSeriesReadProtocol.LatestTool, latest,
            async () => await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadLatestSampleAsync(latest, token)),
            new LatestSampleResult(rows.LastOrDefault()), token);
        var aggregate = new AggregateSamplesRequest(partition, TimeSeriesChunkQualificationRf3Data.Set,
            TimeSeriesChunkQualificationRf3Data.Series, TimeSeriesChunkQualificationRf3Data.From, TimeSeriesChunkQualificationRf3Data.Until);
        await AllAsync(callers, partition, TimeSeriesReadProtocol.AggregateTool, aggregate,
            async () => await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.AggregateSamplesAsync(aggregate, token)),
            TimeSeriesChunkQualificationRf3Data.Aggregate(sibling, state.Phase), token);
        var windows = new AggregateSampleWindowsRequest(partition, TimeSeriesChunkQualificationRf3Data.Set,
            TimeSeriesChunkQualificationRf3Data.Series, TimeSeriesChunkQualificationRf3Data.From, TimeSeriesChunkQualificationRf3Data.Until,
            TimeSeriesChunkQualificationRf3Data.WindowWidth, TimeSeriesChunkQualificationRf3Data.ReadLimit, TimeSeriesChunkQualificationRf3Data.DenseWindowCount);
        await AllAsync(callers, partition, TimeSeriesReadProtocol.WindowsTool, windows,
            async () => await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.AggregateSampleWindowsAsync(windows, token)),
            new SampleAggregateWindowsResult([new(TimeSeriesChunkQualificationRf3Data.From, TimeSeriesChunkQualificationRf3Data.Until,
                TimeSeriesChunkQualificationRf3Data.Aggregate(sibling, state.Phase))]), token);
        var retention = new ReadSampleRetentionRequest(partition, TimeSeriesChunkQualificationRf3Data.Set, TimeSeriesChunkQualificationRf3Data.Series);
        await AllAsync(callers, partition, TimeSeriesReadProtocol.RetentionTool, retention,
            async () => await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadSampleRetentionAsync(retention, token)),
            TimeSeriesChunkQualificationRf3Data.Retention(sibling, state.Phase, state.Purged, state.HasMore), token);
    }
    internal static Task TagRepairAsync(RequestCqrsRf3Callers reader, TimeSeriesChunkQualificationRf3State state, CancellationToken token)
    {
        var request = TimeSeriesChunkQualificationRf3Data.Request(state.Partitions[TimeSeriesChunkQualificationRf3Data.SiblingScope], state.Window) with
        {
            MinimumToken = state.Originals[TimeSeriesChunkQualificationRf3Data.SiblingScope].Receipt.Token,
            TagPointer = TimeSeriesChunkQualificationRf3Data.PrivatePath,
            TagValue = "beta"
        };
        return ChunkAsync(reader, state, request, true, true, token);
    }
    private static async Task<SampleRecord[]> RawAsync(RequestCqrsRf3Callers callers, ReadSamplesRequest request, CancellationToken token)
        => await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadSamplesAsync(request, token));
    private static async Task AllAsync<TRequest, TResult>(RequestCqrsRf3Callers callers, PartitionRef partition, string tool, TRequest request,
        Func<Task<TResult>> sdk, TResult expected, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(expected, await sdk());
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<TResult>(await callers.Mcp.CallAsync(tool, request, token))).Value);
        var call = SqlRf3Protocol.Call(partition, tool, request);
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.SdkAsync<TResult>(callers.Sdk, call, token));
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.McpAsync<TResult>(callers.Mcp, call, token));
    }
    private static async Task ChunkAsync(RequestCqrsRf3Callers callers, TimeSeriesChunkQualificationRf3State state,
        ReadSampleChunkWindowRequest request, bool sibling, bool projected, CancellationToken token)
    {
        var call = SqlRf3Protocol.Call(request.Partition, SampleChunkProtocol.ReadTool, request);
        var results = new[]
        {
            await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadSampleChunkWindowAsync(request, token)),
            (await McpCallerAssertions.SuccessAsync<SampleChunkWindowResult>(await callers.Mcp.CallAsync(SampleChunkProtocol.ReadTool, request, token))).Value,
            await SqlRf3Protocol.SdkAsync<SampleChunkWindowResult>(callers.Sdk, call, token),
            await SqlRf3Protocol.McpAsync<SampleChunkWindowResult>(callers.Mcp, call, token)
        };
        foreach (var actual in results)
        {
            await Assert.That(actual.CutPosition).IsGreaterThan(0L);
            await SqlRf3Protocol.EqualAsync(TimeSeriesChunkQualificationRf3Data.Chunk(state.Window, sibling, state.Phase, projected, actual.CutPosition), actual);
            var expected = TimeSeriesChunkQualificationRf3Data.Rows(sibling, state.Phase, projected);
            await BitsAsync(expected, actual.Records.ToArray());
        }
    }
    private static async Task BitsAsync(SampleRecord[] expected, SampleRecord[] actual)
    {
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(BitConverter.DoubleToInt64Bits(actual[index].Sample.Value)).IsEqualTo(BitConverter.DoubleToInt64Bits(expected[index].Sample.Value));
            await Assert.That(actual[index].Sample.Timestamp.Ticks).IsEqualTo(expected[index].Sample.Timestamp.Ticks);
            await Assert.That(actual[index].Sample.Timestamp.Offset).IsEqualTo(expected[index].Sample.Timestamp.Offset);
        }
    }
}
