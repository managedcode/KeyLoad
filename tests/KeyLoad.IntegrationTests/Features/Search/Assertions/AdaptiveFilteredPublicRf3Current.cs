using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class AdaptiveFilteredPublicRf3Current
{
    internal static async Task ObserveAsync(RequestCqrsRf3Callers administrator, AdaptiveFilteredPublicRf3State state,
        Func<Task> operation, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token));
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(() => RowsAsync(administrator, state, token), failures);
        await ServerFailureObserver.ObserveAsync(async () => await AdaptiveFilteredPublicRf3Equal.RequireAsync(before,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token))), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task RowsAsync(RequestCqrsRf3Callers administrator, AdaptiveFilteredPublicRf3State state, CancellationToken token)
    {
        var expected = AdaptiveFilteredPublicRf3Recipe.Expected(state.Scenario.Partition,
            Enumerable.Range(0, AdaptiveFilteredPublicRf3Protocol.Count), AdaptiveFilteredPublicRf3Protocol.Count);
        var request = AdaptiveFilteredPublicRf3Recipe.Search(state, null, AdaptiveFilteredPublicRf3Protocol.Count);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () => await AdaptiveFilteredPublicRf3Equal.RequireAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.SearchAsync(request, token))), failures);
        await ServerFailureObserver.ObserveAsync(async () => await AdaptiveFilteredPublicRf3Equal.RequireAsync(expected,
            (await McpCallerAssertions.SuccessAsync<RankedDocument[]>(await administrator.Mcp.CallAsync(
                McpCallerTools.SearchExecute, request, token))).Value), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task ReplayAsync(RequestCqrsRf3Callers administrator, AdaptiveFilteredPublicRf3State state, CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var batch in state.Batches)
        {
            await ServerFailureObserver.ObserveAsync(() => ObserveAsync(administrator, state, async () =>
                await AdaptiveFilteredPublicRf3Equal.RequireAsync(batch.Receipt,
                    await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(batch.Request, token))), token), failures);
            await ServerFailureObserver.ObserveAsync(() => ObserveAsync(administrator, state, async () =>
                await AdaptiveFilteredPublicRf3Equal.RequireAsync(batch.Receipt,
                    (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await administrator.Mcp.CallAsync(
                        McpCallerTools.DocumentsCommit, batch.Request, token))).Value), token), failures);
        }
        await ServerFailureObserver.ObserveAsync(() => ObserveAsync(administrator, state, async () =>
            await AdaptiveFilteredPublicRf3Equal.RequireAsync(state.Published,
                await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.MaintainAnnIndexAsync(state.Pin, token))), token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
