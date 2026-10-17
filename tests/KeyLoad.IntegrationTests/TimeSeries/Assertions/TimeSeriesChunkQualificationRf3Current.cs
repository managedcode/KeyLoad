using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class TimeSeriesChunkQualificationRf3Current
{
    internal static async Task ObserveAsync(RequestCqrsRf3Callers root, TimeSeriesChunkQualificationRf3State state,
        Func<Task> action, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(action, failures);
        await ServerFailureObserver.ObserveAsync(() => CheckAsync(root, state, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task CheckAsync(RequestCqrsRf3Callers root, TimeSeriesChunkQualificationRf3State state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => ImageAsync(root, state, token), failures);
        foreach (var original in state.Originals)
        {
            await ServerFailureObserver.ObserveAsync(async () => await SqlRf3Protocol.EqualAsync(original.Receipt,
                await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(original.Command, token))), failures);
            await ServerFailureObserver.ObserveAsync(() => ImageAsync(root, state, token), failures);
            await ServerFailureObserver.ObserveAsync(async () => await SqlRf3Protocol.EqualAsync(original.Receipt,
                (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await root.Mcp.CallAsync(McpCallerTools.DocumentsCommit,
                    original.Command, token))).Value), failures);
            await ServerFailureObserver.ObserveAsync(() => ImageAsync(root, state, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task ImageAsync(RequestCqrsRf3Callers root, TimeSeriesChunkQualificationRf3State state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => TimeSeriesChunkQualificationRf3Reads.RunAsync(root, state, false, token), failures);
        for (var index = 0; index < state.Partitions.Length; index++)
        {
            await ServerFailureObserver.ObserveAsync(async () => await SqlRf3Protocol.EqualAsync(state.Outboxes[index],
                await TimeSeriesChunkQualificationRf3Setup.ReadOutboxAsync(root, state, index, token)), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
