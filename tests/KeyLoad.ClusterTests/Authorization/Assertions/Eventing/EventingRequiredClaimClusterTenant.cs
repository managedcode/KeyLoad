using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.Authorization;

internal static class EventingRequiredClaimClusterTenant
{
    internal static async Task RefuseAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        EventingRequiredClaimClusterState state, CancellationToken token)
    {
        const string detail = "The principal cannot perform this operation in this scope.";
        var partition = state.Input.Partition with
        {
            TenantId = "forged-" + state.Input.Partition.TenantId
        };
        var request = new InspectMessageRequest(state.Input.Lane with
        {
            Partition = partition
        }, "first");
        var sql = SqlClusterProtocol.Call(partition, McpCallerTools.MessagesInspect, request);
        await EventingRequiredClaimClusterCurrent.ObserveAsync(root, state, async () =>
            await EventingRequiredClaimClusterRefusals.SdkAsync(await reader.Sdk.InspectAsync(request, token), ErrorCode.PermissionDenied, detail), token);
        await EventingRequiredClaimClusterCurrent.ObserveAsync(root, state, () => EventingRequiredClaimClusterRefusals.McpAsync(
            () => reader.Mcp.CallAsync(McpCallerTools.MessagesInspect, request, token), state, ErrorCode.PermissionDenied, detail), token);
        await EventingRequiredClaimClusterCurrent.ObserveAsync(root, state, async () =>
            await EventingRequiredClaimClusterRefusals.SqlAsync(await reader.Sdk.ExecuteSqlAsync(sql, token), ErrorCode.PermissionDenied, detail), token);
        await EventingRequiredClaimClusterCurrent.ObserveAsync(root, state, () => EventingRequiredClaimClusterRefusals.McpAsync(
            () => reader.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token), state, ErrorCode.PermissionDenied, detail), token);
    }
}
