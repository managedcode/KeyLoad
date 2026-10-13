using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextAuthorizedQueryR39Replay
{
    internal static async Task RunAsync(RequestCqrsRf3Callers admin, NativeTextAuthorizedQueryR39State state,
        Func<Task> current, CancellationToken token)
    {
        await current();
        await SqlRf3Protocol.EqualAsync(state.Original.Receipt, await McpCallerAssertions.SdkSuccessAsync(
            await admin.Sdk.CommitAsync(state.Original.Original, token)));
        await current();
        await SqlRf3Protocol.EqualAsync(state.Original.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Original.Original, token))).Value);
        await current();
        foreach (var path in new[] { NativeTextMaintenancePath.Sdk, NativeTextMaintenancePath.Mcp })
        {
            var before = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.StatusAsync(token));
            var result = await NativeTextMaintenanceRf3Call.ExecuteAsync(admin.Sdk, admin.Mcp,
                state.Original.Build, path, token);
            var after = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.StatusAsync(token));
            await NativeTextMaintenanceRf3ReplayAssertions.StableAsync(result, state.Original.Built);
            await NativeTextMaintenanceRf3ReplayAssertions.SourceAsync(result.Source!, state.Original.Built.Source!, before, after, 2L);
            await current();
        }
    }

    internal static async Task DeniedAsync(RequestCqrsRf3Callers reader, RequestCqrsRf3Callers admin,
        NativeTextAuthorizedQueryR39State state, OutboxStatus baseline, CancellationToken token)
    {
        foreach (var selected in new[] { true, false })
        {
            var request = new SearchRequest(state.Original.Scenario.Partition,
                NativeTextMaintenanceRf3Scenario.Collection, NativeTextMaintenanceRf3Scenario.Field, "needle",
                TextIndex: selected ? new(state.Original.Build.Consumer, state.Original.Build.IndexGeneration) : null);
            var sdk = await reader.Sdk.SearchAsync(request, token);
            await Assert.That(sdk.IsFailed).IsTrue();
            await Assert.That(sdk.Value).IsNull();
            await Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
            await NativeTextAuthorizedQueryR39Pages.AdminAsync(admin, state, baseline, token);
            await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(McpCallerTools.SearchExecute, request, token),
                ErrorCode.PermissionDenied, dispatched: true);
            await NativeTextAuthorizedQueryR39Pages.AdminAsync(admin, state, baseline, token);
        }
    }
}
