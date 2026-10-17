using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SeriesExtremeAggregateCurrent
{
    private const long FirstSequence = 1;
    private const long LateSequence = 6;
    private const long MaximumSequence = 7;
    private const long MinimumSequence = 8;
    private const int RangeLimit = 8;
    private const long SealedGeneration = 1;
    private const long SealedRevision = 9;
    private const long SealedSequence = 8;
    private const long NoPosition = 0;
    private const int FirstIndex = 0;
    private const int SecondIndex = 1;

    internal static SampleRecord[] Rows(SeriesExtremeAggregateState state)
    {
        var rows = state.Initial.Select((sample, index) => new SampleRecord(TimeSeriesRf3Scenario.Series, sample,
            FirstSequence + index, TimeSeriesRf3Scenario.PublicTags)).ToList();
        rows.Add(new(TimeSeriesRf3Scenario.Series, state.Late[FirstIndex], LateSequence, TimeSeriesRf3Scenario.PublicTags));
        if (state.HasExtrema)
        {
            rows.Add(new(TimeSeriesRf3Scenario.Series, state.Extrema[FirstIndex], MaximumSequence, TimeSeriesRf3Scenario.PublicTags));
            rows.Add(new(TimeSeriesRf3Scenario.Series, state.Extrema[SecondIndex], MinimumSequence, TimeSeriesRf3Scenario.PublicTags));
        }
        return rows.OrderBy(row => row.Sample.Timestamp.UtcTicks).ThenBy(row => row.Sequence).ToArray();
    }

    internal static async Task RowsAsync(RequestCqrsRf3Callers caller, SeriesExtremeAggregateState state, CancellationToken token)
    {
        var request = new ReadSamplesRequest(state.Scenario.Partition, TimeSeriesRf3Scenario.Set,
            TimeSeriesRf3Scenario.Series, DateTimeOffset.MinValue, DateTimeOffset.MaxValue, RangeLimit);
        var expected = Rows(state);
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.ReadSamplesAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<SampleRecord[]>(
            await caller.Mcp.CallAsync(McpCallerTools.SeriesRead, request, token))).Value);
    }

    private static async Task ChunkAsync(RequestCqrsRf3Callers caller, SeriesExtremeAggregateState state,
        CancellationToken token)
    {
        if (!state.Sealed)
        {
            return;
        }
        var request = new ReadSampleChunkWindowRequest(state.Scenario.Partition, TimeSeriesRf3Scenario.Set,
            TimeSeriesRf3Scenario.Series, state.WindowId, null, null, Limit: RangeLimit);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.ReadSampleChunkWindowAsync(request, token));
        await Assert.That(sdk.CutPosition).IsGreaterThan(NoPosition);
        await SqlRf3Protocol.EqualAsync(new SampleChunkWindowResult(state.WindowId, DateTimeOffset.MinValue,
            DateTimeOffset.MaxValue, SealedGeneration, SealedRevision, SealedSequence, null,
            [.. Rows(state).Where(row => row.Sample.Timestamp.UtcTicks < DateTimeOffset.MaxValue.UtcTicks)], sdk.CutPosition), sdk);
        var mcp = (await McpCallerAssertions.SuccessAsync<SampleChunkWindowResult>(
            await caller.Mcp.CallAsync(SampleChunkProtocol.ReadTool, request, token))).Value;
        await Assert.That(mcp.CutPosition).IsGreaterThan(NoPosition);
        await SqlRf3Protocol.EqualAsync(new SampleChunkWindowResult(state.WindowId, DateTimeOffset.MinValue,
            DateTimeOffset.MaxValue, SealedGeneration, SealedRevision, SealedSequence, null,
            [.. Rows(state).Where(row => row.Sample.Timestamp.UtcTicks < DateTimeOffset.MaxValue.UtcTicks)], mcp.CutPosition), mcp);
    }

    internal static async Task RequireAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers reader,
        SeriesExtremeAggregateState state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => RowsAsync(administrator, state, token), failures);
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => RowsAsync(reader, state, token), failures);
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => ChunkAsync(administrator, state, token), failures);
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => ChunkAsync(reader, state, token), failures);
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(state.Outbox).IsNotNull();
            await SqlRf3Protocol.EqualAsync(state.Outbox!, await McpCallerAssertions.SdkSuccessAsync(
                await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token)));
        }, failures);
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(state.Outbox).IsNotNull();
            await SqlRf3Protocol.EqualAsync(state.Outbox!, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
                await administrator.Mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Scenario.Partition), token))).Value);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
