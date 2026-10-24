using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class EventingRequiredClaimClusterExport
{
    internal static async Task RefuseAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        EventingRequiredClaimClusterState state, CancellationToken token)
    {
        await EventingRequiredClaimClusterCurrent.ObserveAsync(root, state, async () =>
            await EventingRequiredClaimClusterRefusals.SdkAsync(await reader.Sdk.BackupAsync(token),
                ErrorCode.PermissionDenied, "Cluster administration is required."), token);
        await EventingRequiredClaimClusterCurrent.ObserveAsync(root, state, () =>
            EventingRequiredClaimClusterRefusals.McpAsync(() => reader.Mcp.CallWithoutBodyAsync(McpCallerTools.AdminBackup, token),
                state, ErrorCode.PermissionDenied), token);
    }
}
