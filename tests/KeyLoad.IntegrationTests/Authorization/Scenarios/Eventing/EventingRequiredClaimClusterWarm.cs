using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class EventingRequiredClaimClusterWarm
{
    internal static async Task RunAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        EventingRequiredClaimClusterState state, CancellationToken token)
    {
        var input = state.Input;
        await RequiredProcessingPolicyClusterSetup.PolicyAsync(root, input, RequiredProcessingPolicyClusterState.After, token);
        await EventingRequiredClaimClusterRefusals.ReceiveAsync(root, reader, state, ErrorCode.PermissionDenied, RequiredProcessingPolicyClusterState.Denied, token);
        await RequiredProcessingPolicyClusterSetup.PolicyAsync(root, input, RequiredProcessingPolicyClusterState.Before, token);
        await EventingRequiredClaimClusterRefusals.ReceiveAsync(root, reader, state, ErrorCode.PermissionDenied, RequiredProcessingPolicyClusterState.Denied, token);
        await RequiredProcessingPolicyClusterSetup.PrincipalAsync(root, input, 2L, true, token);
        await EventingRequiredClaimClusterRefusals.ReceiveAsync(root, reader, state, ErrorCode.Unauthenticated, "The credential is unavailable or expired.", token);
        await RequiredProcessingPolicyClusterSetup.PrincipalAsync(root, input, 3L, false, token);
        await EventingRequiredClaimClusterRefusals.ReceiveAsync(root, reader, state, ErrorCode.PermissionDenied, RequiredProcessingPolicyClusterState.EpochChanged, token);
        var delivery = await RequiredProcessingPolicyClusterRoutes.ReceiveAsync(reader, input, "first", token);
        var forged = input.Processing(delivery, "first") with
        {
            Token = delivery.Token + ".forged"
        };
        await ForgedAsync(root, reader, input, forged, token);
        state.Healthy = input.Processing(delivery, "first") with
        {
            HandlerScope = EventingRequiredClaimClusterState.Handler
        };
        state.Receipt = await RequiredProcessingPolicyClusterRoutes.CompleteAsync(reader, input, state.Healthy, "first", token);
        state.Completed = true;
        await EventingRequiredClaimClusterFinal.RunAsync(root, reader, state, token);
    }
    private static async Task ForgedAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        RequiredProcessingPolicyClusterState input, ProcessingRequest request, CancellationToken token)
    {
        var sql = SqlClusterProtocol.Call(input.Partition, RequiredProcessingPolicyClusterState.Tool, request);
        await RequiredProcessingPolicyClusterValues.ObserveAsync(root, input, 0, async () =>
        {
            await EventingRequiredClaimClusterRefusals.SdkAsync(await reader.Sdk.CommitProcessingAsync(request, token),
                ErrorCode.TokenInvalidated, "The signed token is invalid.");
            var state = new EventingRequiredClaimClusterState(input);
            await EventingRequiredClaimClusterRefusals.McpAsync(() => reader.Mcp.CallAsync(RequiredProcessingPolicyClusterState.Tool, request, token), state, ErrorCode.TokenInvalidated);
            await EventingRequiredClaimClusterRefusals.SqlAsync(await reader.Sdk.ExecuteSqlAsync(sql, token), ErrorCode.TokenInvalidated, "The signed token is invalid.");
            await EventingRequiredClaimClusterRefusals.McpAsync(() => reader.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token), state, ErrorCode.TokenInvalidated);
        }, token);
    }
}
