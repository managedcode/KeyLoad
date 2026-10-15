using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class EventingRequiredClaimRf3Warm
{
    internal static async Task RunAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        EventingRequiredClaimRf3State state, CancellationToken token)
    {
        var input = state.Input;
        await RequiredProcessingPolicyRf3Setup.PolicyAsync(root, input, RequiredProcessingPolicyRf3State.After, token);
        await EventingRequiredClaimRf3Refusals.ReceiveAsync(root, reader, state, ErrorCode.PermissionDenied, RequiredProcessingPolicyRf3State.Denied, token);
        await RequiredProcessingPolicyRf3Setup.PolicyAsync(root, input, RequiredProcessingPolicyRf3State.Before, token);
        await EventingRequiredClaimRf3Refusals.ReceiveAsync(root, reader, state, ErrorCode.PermissionDenied, RequiredProcessingPolicyRf3State.Denied, token);
        await RequiredProcessingPolicyRf3Setup.PrincipalAsync(root, input, 2L, true, token);
        await EventingRequiredClaimRf3Refusals.ReceiveAsync(root, reader, state, ErrorCode.Unauthenticated, "The credential is unavailable or expired.", token);
        await RequiredProcessingPolicyRf3Setup.PrincipalAsync(root, input, 3L, false, token);
        await EventingRequiredClaimRf3Refusals.ReceiveAsync(root, reader, state, ErrorCode.PermissionDenied, RequiredProcessingPolicyRf3State.EpochChanged, token);
        var delivery = await RequiredProcessingPolicyRf3Routes.ReceiveAsync(reader, input, "first", token);
        var forged = input.Processing(delivery, "first") with
        {
            Token = delivery.Token + ".forged"
        };
        await ForgedAsync(root, reader, input, forged, token);
        state.Healthy = input.Processing(delivery, "first") with
        {
            HandlerScope = EventingRequiredClaimRf3State.Handler
        };
        state.Receipt = await RequiredProcessingPolicyRf3Routes.CompleteAsync(root, reader, input, state.Healthy, "first", token);
        state.Completed = true;
        await EventingRequiredClaimRf3Final.RunAsync(root, reader, state, token);
    }
    private static async Task ForgedAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        RequiredProcessingPolicyRf3State input, ProcessingRequest request, CancellationToken token)
    {
        var sql = SqlRf3Protocol.Call(input.Partition, RequiredProcessingPolicyRf3State.Tool, request);
        await RequiredProcessingPolicyRf3Values.ObserveAsync(root, input, 0, async () =>
        {
            await EventingRequiredClaimRf3Refusals.SdkAsync(await reader.Sdk.CommitProcessingAsync(request, token),
                ErrorCode.TokenInvalidated, "The signed token is invalid.");
            var state = new EventingRequiredClaimRf3State(input);
            await EventingRequiredClaimRf3Refusals.McpAsync(() => reader.Mcp.CallAsync(RequiredProcessingPolicyRf3State.Tool, request, token), state, ErrorCode.TokenInvalidated);
            await EventingRequiredClaimRf3Refusals.SqlAsync(await reader.Sdk.ExecuteSqlAsync(sql, token), ErrorCode.TokenInvalidated, "The signed token is invalid.");
            await EventingRequiredClaimRf3Refusals.McpAsync(() => reader.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token), state, ErrorCode.TokenInvalidated);
        }, token);
    }
}
