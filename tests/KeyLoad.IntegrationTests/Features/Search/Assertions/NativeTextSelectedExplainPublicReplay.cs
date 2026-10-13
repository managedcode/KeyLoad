using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextSelectedExplainPublicReplay
{
    internal static async Task RunAsync(RequestCqrsRf3Callers caller,
        NativeTextSelectedExplainPublicState state, CancellationToken token)
    {
        await CurrentAsync(caller, state, token);
        foreach (var pair in new[] { (state.Published, state.Publication), (state.Deleted, state.Deletion) })
        {
            await SqlRf3Protocol.EqualAsync(pair.Item2, await McpCallerAssertions.SdkSuccessAsync(
                await caller.Sdk.CommitAsync(pair.Item1, token)));
            await CurrentAsync(caller, state, token);
            await SqlRf3Protocol.EqualAsync(pair.Item2, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await caller.Mcp.CallAsync(McpCallerTools.DocumentsCommit, pair.Item1, token))).Value);
            await CurrentAsync(caller, state, token);
        }
        foreach (var path in new[] { NativeTextMaintenancePath.Sdk, NativeTextMaintenancePath.Mcp })
        {
            var before = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
            var result = await NativeTextMaintenanceRf3Call.ExecuteAsync(caller.Sdk, caller.Mcp, state.Restore, path, token);
            var after = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
            await NativeTextMaintenanceRf3ReplayAssertions.StableAsync(result, state.Restored);
            await NativeTextMaintenanceRf3ReplayAssertions.SourceAsync(result.Source!, state.Restored.Source!, before, after);
            await CurrentAsync(caller, state, token);
        }
        await OldBuildAsync(caller, state, token);
    }

    private static Task CurrentAsync(RequestCqrsRf3Callers caller,
        NativeTextSelectedExplainPublicState state, CancellationToken token)
        => NativeTextSelectedExplainPublicPages.CurrentAsync(caller, state.Restore, true, true, state.Outbox, token);

    private static async Task OldBuildAsync(RequestCqrsRf3Callers caller,
        NativeTextSelectedExplainPublicState state, CancellationToken token)
    {
        var sdk = await caller.Sdk.MaintainTextIndexAsync(state.Build, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.HistoryUnavailable));
        await Assert.That(sdk.Value).IsNull();
        await CurrentAsync(caller, state, token);
        await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(TextIndexMaintenanceProtocol.ToolName,
            state.Build, token), ErrorCode.HistoryUnavailable, dispatched: true);
        await CurrentAsync(caller, state, token);
    }
}
