using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.TimeSeries;

internal static class TimeSeriesChunkQualificationClusterReads
{
    internal static async Task RunAsync(RequestCqrsClusterCallers callers, TimeSeriesChunkQualificationClusterState state,
        bool projected, CancellationToken token)
    {
        var failures = new List<Exception>();
        for (var index = 0; index < state.Partitions.Length; index++)
        {
            await ServerFailureObserver.ObserveAsync(() => ScopeAsync(callers, state, index, projected, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task ScopeAsync(RequestCqrsClusterCallers callers, TimeSeriesChunkQualificationClusterState state,
        int index, bool projected, CancellationToken token)
    {
        var partition = state.Partitions[index];
        var sibling = index == TimeSeriesChunkQualificationClusterData.SiblingScope;
        var request = TimeSeriesChunkQualificationClusterData.Request(partition, state.Window) with
        {
            MinimumToken = state.Originals[index].Receipt.Token
        };
        if (!sibling && state.Phase == TimeSeriesChunkQualificationClusterData.DroppedPhase)
        {
            await TimeSeriesChunkQualificationClusterRefusals.ChunkAsync(callers, request, ErrorCode.HistoryUnavailable, projected ? state.Reader.Secret : state.RootCredential,
                TimeSeriesChunkQualificationClusterData.Missing, TimeSeriesChunkQualificationClusterData.SafeUnavailable, token);
        }
        else
        {
            await ChunkAsync(callers, state, request, sibling, projected, token);
        }
        var rows = TimeSeriesChunkQualificationClusterData.Rows(sibling, state.Phase, projected);
        var raw = new ReadSamplesRequest(partition, TimeSeriesChunkQualificationClusterData.Set, TimeSeriesChunkQualificationClusterData.Series,
            TimeSeriesChunkQualificationClusterData.From, TimeSeriesChunkQualificationClusterData.Until, TimeSeriesChunkQualificationClusterData.ReadLimit);
        await AllAsync(callers, partition, McpCallerTools.SeriesRead, raw, () => RawAsync(callers, raw, token), rows, token);
        var latest = new ReadLatestSampleRequest(partition, TimeSeriesChunkQualificationClusterData.Set, TimeSeriesChunkQualificationClusterData.Series);
        await AllAsync(callers, partition, TimeSeriesReadProtocol.LatestTool, latest,
            async () => await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadLatestSampleAsync(latest, token)),
            new LatestSampleResult(rows.LastOrDefault()), token);
        var aggregate = new AggregateSamplesRequest(partition, TimeSeriesChunkQualificationClusterData.Set,
            TimeSeriesChunkQualificationClusterData.Series, TimeSeriesChunkQualificationClusterData.From, TimeSeriesChunkQualificationClusterData.Until);
        await AllAsync(callers, partition, TimeSeriesReadProtocol.AggregateTool, aggregate,
            async () => await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.AggregateSamplesAsync(aggregate, token)),
            TimeSeriesChunkQualificationClusterData.Aggregate(sibling, state.Phase), token);
        var windows = new AggregateSampleWindowsRequest(partition, TimeSeriesChunkQualificationClusterData.Set,
            TimeSeriesChunkQualificationClusterData.Series, TimeSeriesChunkQualificationClusterData.From, TimeSeriesChunkQualificationClusterData.Until,
            TimeSeriesChunkQualificationClusterData.WindowWidth, TimeSeriesChunkQualificationClusterData.ReadLimit, TimeSeriesChunkQualificationClusterData.DenseWindowCount);
        await AllAsync(callers, partition, TimeSeriesReadProtocol.WindowsTool, windows,
            async () => await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.AggregateSampleWindowsAsync(windows, token)),
            new SampleAggregateWindowsResult([new(TimeSeriesChunkQualificationClusterData.From, TimeSeriesChunkQualificationClusterData.Until,
                TimeSeriesChunkQualificationClusterData.Aggregate(sibling, state.Phase))]), token);
        var retention = new ReadSampleRetentionRequest(partition, TimeSeriesChunkQualificationClusterData.Set, TimeSeriesChunkQualificationClusterData.Series);
        await AllAsync(callers, partition, TimeSeriesReadProtocol.RetentionTool, retention,
            async () => await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadSampleRetentionAsync(retention, token)),
            TimeSeriesChunkQualificationClusterData.Retention(sibling, state.Phase, state.Purged, state.HasMore), token);
    }
    internal static Task TagRepairAsync(RequestCqrsClusterCallers reader, TimeSeriesChunkQualificationClusterState state, CancellationToken token)
    {
        var request = TimeSeriesChunkQualificationClusterData.Request(state.Partitions[TimeSeriesChunkQualificationClusterData.SiblingScope], state.Window) with
        {
            MinimumToken = state.Originals[TimeSeriesChunkQualificationClusterData.SiblingScope].Receipt.Token,
            TagPointer = TimeSeriesChunkQualificationClusterData.PrivatePath,
            TagValue = "beta"
        };
        return ChunkAsync(reader, state, request, true, true, token);
    }
    private static async Task<SampleRecord[]> RawAsync(RequestCqrsClusterCallers callers, ReadSamplesRequest request, CancellationToken token)
        => await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadSamplesAsync(request, token));
    private static async Task AllAsync<TRequest, TResult>(RequestCqrsClusterCallers callers, PartitionRef partition, string tool, TRequest request,
        Func<Task<TResult>> sdk, TResult expected, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(expected, await sdk());
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<TResult>(await callers.Mcp.CallAsync(tool, request, token))).Value);
        var call = SqlClusterProtocol.Call(partition, tool, request);
        await SqlClusterProtocol.EqualAsync(expected, await SqlClusterProtocol.SdkAsync<TResult>(callers.Sdk, call, token));
        await SqlClusterProtocol.EqualAsync(expected, await SqlClusterProtocol.McpAsync<TResult>(callers.Mcp, call, token));
    }
    private static async Task ChunkAsync(RequestCqrsClusterCallers callers, TimeSeriesChunkQualificationClusterState state,
        ReadSampleChunkWindowRequest request, bool sibling, bool projected, CancellationToken token)
    {
        var call = SqlClusterProtocol.Call(request.Partition, SampleChunkProtocol.ReadTool, request);
        var results = new[]
        {
            await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadSampleChunkWindowAsync(request, token)),
            (await McpCallerAssertions.SuccessAsync<SampleChunkWindowResult>(await callers.Mcp.CallAsync(SampleChunkProtocol.ReadTool, request, token))).Value,
            await SqlClusterProtocol.SdkAsync<SampleChunkWindowResult>(callers.Sdk, call, token),
            await SqlClusterProtocol.McpAsync<SampleChunkWindowResult>(callers.Mcp, call, token)
        };
        foreach (var actual in results)
        {
            await Assert.That(actual.CutPosition).IsGreaterThan(0L);
            await SqlClusterProtocol.EqualAsync(TimeSeriesChunkQualificationClusterData.Chunk(state.Window, sibling, state.Phase, projected, actual.CutPosition), actual);
            var expected = TimeSeriesChunkQualificationClusterData.Rows(sibling, state.Phase, projected);
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
