using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class TimeSeriesChunkQualificationClusterCurrent
{
    internal static async Task ObserveAsync(RequestCqrsClusterCallers root, TimeSeriesChunkQualificationClusterState state,
        Func<Task> action, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(action, failures);
        await ServerFailureObserver.ObserveAsync(() => CheckAsync(root, state, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task CheckAsync(RequestCqrsClusterCallers root, TimeSeriesChunkQualificationClusterState state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => ImageAsync(root, state, token), failures);
        foreach (var original in state.Originals)
        {
            await ServerFailureObserver.ObserveAsync(async () => await SqlClusterProtocol.EqualAsync(original.Receipt,
                await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(original.Command, token))), failures);
            await ServerFailureObserver.ObserveAsync(() => ImageAsync(root, state, token), failures);
            await ServerFailureObserver.ObserveAsync(async () => await SqlClusterProtocol.EqualAsync(original.Receipt,
                (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await root.Mcp.CallAsync(McpCallerTools.DocumentsCommit,
                    original.Command, token))).Value), failures);
            await ServerFailureObserver.ObserveAsync(() => ImageAsync(root, state, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task ImageAsync(RequestCqrsClusterCallers root, TimeSeriesChunkQualificationClusterState state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => TimeSeriesChunkQualificationClusterReads.RunAsync(root, state, false, token), failures);
        for (var index = 0; index < state.Partitions.Length; index++)
        {
            await ServerFailureObserver.ObserveAsync(async () => await SqlClusterProtocol.EqualAsync(state.Outboxes[index],
                await TimeSeriesChunkQualificationClusterSetup.ReadOutboxAsync(root, state, index, token)), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
