using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class EventingRequiredClaimRf3Tenant
{
    internal static async Task RefuseAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        EventingRequiredClaimRf3State state, CancellationToken token)
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
        var sql = SqlRf3Protocol.Call(partition, McpCallerTools.MessagesInspect, request);
        await EventingRequiredClaimRf3Current.ObserveAsync(root, state, async () =>
            await EventingRequiredClaimRf3Refusals.SdkAsync(await reader.Sdk.InspectAsync(request, token), ErrorCode.PermissionDenied, detail), token);
        await EventingRequiredClaimRf3Current.ObserveAsync(root, state, () => EventingRequiredClaimRf3Refusals.McpAsync(
            () => reader.Mcp.CallAsync(McpCallerTools.MessagesInspect, request, token), state, ErrorCode.PermissionDenied, detail), token);
        await EventingRequiredClaimRf3Current.ObserveAsync(root, state, async () =>
            await EventingRequiredClaimRf3Refusals.SqlAsync(await reader.Sdk.ExecuteSqlAsync(sql, token), ErrorCode.PermissionDenied, detail), token);
        await EventingRequiredClaimRf3Current.ObserveAsync(root, state, () => EventingRequiredClaimRf3Refusals.McpAsync(
            () => reader.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token), state, ErrorCode.PermissionDenied, detail), token);
    }
}
