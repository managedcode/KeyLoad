using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextVectorFusionR40Replay
{
    internal static async Task AllAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers reader,
        NativeTextVectorFusionR40State state, long epoch, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(state.Receipt, await McpCallerAssertions.SdkSuccessAsync(
            await admin.Sdk.CommitAsync(state.Command, token)));
        await NativeTextVectorFusionR40Pages.MatrixAsync(admin, reader, state, epoch, false, token);
        await SqlRf3Protocol.EqualAsync(state.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Command, token))).Value);
        await NativeTextVectorFusionR40Pages.MatrixAsync(admin, reader, state, epoch, false, token);
        foreach (var path in new[] { NativeTextMaintenancePath.Sdk, NativeTextMaintenancePath.Mcp })
        {
            var before = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.StatusAsync(token));
            var replay = await NativeTextMaintenanceRf3Call.ExecuteAsync(admin.Sdk, admin.Mcp, state.Build, path, token);
            var after = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.StatusAsync(token));
            await NativeTextMaintenanceRf3ReplayAssertions.StableAsync(replay, state.Built);
            await NativeTextMaintenanceRf3ReplayAssertions.SourceAsync(replay.Source!, state.Built.Source!, before, after);
            await NativeTextVectorFusionR40Pages.MatrixAsync(admin, reader, state, epoch, false, token);
        }
    }
}
