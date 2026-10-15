using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class EventingRequiredClaimRf3Export
{
    internal static async Task RefuseAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        EventingRequiredClaimRf3State state, CancellationToken token)
    {
        await EventingRequiredClaimRf3Current.ObserveAsync(root, state, async () =>
            await EventingRequiredClaimRf3Refusals.SdkAsync(await reader.Sdk.BackupAsync(token),
                ErrorCode.PermissionDenied, "Cluster administration is required."), token);
        await EventingRequiredClaimRf3Current.ObserveAsync(root, state, () =>
            EventingRequiredClaimRf3Refusals.McpAsync(() => reader.Mcp.CallWithoutBodyAsync(McpCallerTools.AdminBackup, token),
                state, ErrorCode.PermissionDenied), token);
    }
}
