using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class ChunkMinimumRf3Pages
{
    private const long FirstGeneration = 1;
    private const long NoPosition = 0;

    internal static async Task ReadAsync(RequestCqrsRf3Callers callers, ChunkMinimumRf3Window window,
        ReadSampleChunkWindowRequest request, bool official, CancellationToken token)
    {
        var actual = official
            ? (await McpCallerAssertions.SuccessAsync<SampleChunkWindowResult>(
                await callers.Mcp.CallAsync(SampleChunkProtocol.ReadTool, request, token))).Value
            : await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadSampleChunkWindowAsync(request, token));
        await Assert.That(actual.CutPosition).IsGreaterThan(NoPosition);
        await SqlRf3Protocol.EqualAsync(new SampleChunkWindowResult(window.Id, TimeSeriesRf3Scenario.Start,
            DateTimeOffset.MaxValue, FirstGeneration, window.Revision, window.Sequence, null,
            window.Rows, actual.CutPosition), actual);
    }

    internal static async Task ConserveAsync(RequestCqrsRf3Callers administrator,
        ChunkMinimumRf3State state, CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var window in state.Windows)
        {
            foreach (var official in new[] { false, true })
            {
                await ServerFailureObserver.ObserveAsync(() => ReadAsync(administrator, window,
                    window.Request(state.Partition), official, token), failures);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var request = new ReadSamplesRequest(state.Partition, TimeSeriesRf3Scenario.Set,
                        window.Series, TimeSeriesRf3Scenario.Start, DateTimeOffset.MaxValue, window.Rows.Length);
                    var rows = official
                        ? (await McpCallerAssertions.SuccessAsync<SampleRecord[]>(await administrator.Mcp.CallAsync(
                            McpCallerTools.SeriesRead, request, token))).Value
                        : await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadSamplesAsync(request, token));
                    await SqlRf3Protocol.EqualAsync(window.Rows.ToArray(), rows);
                }, failures);
            }
        }
        await ServerFailureObserver.ObserveAsync(async () => await SqlRf3Protocol.EqualAsync(state.Outbox,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(state.Partition, token))), failures);
        await ServerFailureObserver.ObserveAsync(async () => await SqlRf3Protocol.EqualAsync(state.Outbox,
            (await McpCallerAssertions.SuccessAsync<OutboxStatus>(await administrator.Mcp.CallAsync(
                McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Partition), token))).Value), failures);
        await ServerFailureObserver.ObserveAsync(async () => await SqlRf3Protocol.EqualAsync(state.Owner,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadAtomicPartitionPlacementAsync(
                new(1, state.Partition), token))), failures);
        await ServerFailureObserver.ObserveAsync(async () => await SqlRf3Protocol.EqualAsync(state.Owner,
            (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(await administrator.Mcp.CallAsync(
                McpCallerTools.AdminPartitionPlacementRead, new AtomicPartitionPlacementReadRequest(1, state.Partition), token))).Value), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task ObserveAsync(Func<Task> primary, RequestCqrsRf3Callers administrator,
        ChunkMinimumRf3State state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(primary, failures);
        await ServerFailureObserver.ObserveAsync(() => ConserveAsync(administrator, state, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
