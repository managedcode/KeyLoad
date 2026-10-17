using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class HeavyRouteRf3Current
{
    internal static async Task ObserveAsync(RequestCqrsRf3Callers root, HeavyRouteRf3State state, Func<Task> operation, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        foreach (var id in new[] { HeavyRouteRf3State.ItemA, HeavyRouteRf3State.ItemB, HeavyRouteRf3State.ItemC })
        {
            await ServerFailureObserver.ObserveAsync(async () =>
                await SqlRf3Protocol.EqualAsync(new DocumentReadResult(new(state.Vertex(id), 1L, "{}", false, [])),
                    await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.GetAsync(state.Vertex(id), state.Receipt.Token, token))), failures);
        }
        await ServerFailureObserver.ObserveAsync(async () => await SqlRf3Protocol.EqualAsync(state.Outbox,
            await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(state.Partition, token))), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static Task ReplayAsync(RequestCqrsRf3Callers root, HeavyRouteRf3State state, CancellationToken token)
        => ObserveAsync(root, state, async () =>
        {
            await SqlRf3Protocol.EqualAsync(state.Receipt,
                await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(state.Seed, token)));
            await SqlRf3Protocol.EqualAsync(state.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await root.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Seed, token))).Value);
        }, token);
}
