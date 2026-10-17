using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextReleasedRepairPublicReplay
{
    internal static async Task RunAsync(RequestCqrsRf3Callers caller, NativeTextReleasedRepairPublicState state,
        OutboxStatus baseline, CancellationToken token)
    {
        Task Current() => NativeTextReleasedRepairPublicAssertions.RefuseAsync(caller, state.Original, state.Current, baseline, token);
        await Current();
        await SqlRf3Protocol.EqualAsync(state.Original.Receipt, await McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.CommitAsync(state.Original.Original, token)));
        await Current();
        await SqlRf3Protocol.EqualAsync(state.Original.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await caller.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Original.Original, token))).Value);
        await Current();
        await SqlRf3Protocol.EqualAsync(state.Released, await NativeTextMaintenanceRf3Call.ExecuteAsync(caller.Sdk, caller.Mcp,
            state.Release, NativeTextMaintenancePath.Sdk, token));
        await Current();
        await SqlRf3Protocol.EqualAsync(state.Released, await NativeTextMaintenanceRf3Call.ExecuteAsync(caller.Sdk, caller.Mcp,
            state.Release, NativeTextMaintenancePath.Mcp, token));
        await Current();
        foreach (var path in new[] { NativeTextMaintenancePath.Sdk, NativeTextMaintenancePath.Mcp })
        {
            var before = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
            var actual = await NativeTextMaintenanceRf3Call.ExecuteAsync(caller.Sdk, caller.Mcp, state.Current.Build, path, token);
            var after = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
            await NativeTextMaintenanceRf3ReplayAssertions.StableAsync(actual, state.Current.Built);
            await NativeTextMaintenanceRf3ReplayAssertions.SourceAsync(actual.Source!, state.Current.Built.Source!, before, after, 2L);
            await Current();
        }
    }
}
