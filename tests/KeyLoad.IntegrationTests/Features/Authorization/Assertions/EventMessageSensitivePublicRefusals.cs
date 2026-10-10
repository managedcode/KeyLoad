using System.Text;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class EventMessageSensitivePublicRefusals
{
    internal static Task ReceiveAsync(RequestCqrsRf3Callers callers, EventMessageSensitivePublicState state,
        object request, ErrorCode code, CancellationToken token)
        => request is ReceiveSubscriptionRequest subscription
            ? AllAsync(callers, state, McpCallerTools.SubscriptionsReceive, subscription,
                () => callers.Sdk.ReceiveSubscriptionAsync(subscription, token), code, token)
            : AllAsync(callers, state, McpCallerTools.MessagesReceive, (ReceiveRequest)request,
                () => callers.Sdk.ReceiveAsync((ReceiveRequest)request, token), code, token);

    private static async Task AllAsync<T>(RequestCqrsRf3Callers callers, EventMessageSensitivePublicState state,
        string tool, object request, Func<Task<ManagedCode.Communication.Result<T>>> sdk, ErrorCode code, CancellationToken token)
    {
        await SdkAsync(await sdk(), code);
        var reply = await callers.Mcp.CallAsync(tool, request, token);
        await McpCallerAssertions.ErrorAsync(reply, code, dispatched: code != ErrorCode.Unauthenticated || state.DataAuthority);
        await PrivateAsync(JsonDefaults.Serialize(reply));
        var sql = KeyLoad.IntegrationTests.Features.QueryExecution.SqlRf3Protocol.Call(state.Partition, tool, request);
        await SdkAsync(await callers.Sdk.ExecuteSqlAsync(sql, token), code);
        reply = await callers.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token);
        await McpCallerAssertions.ErrorAsync(reply, code, dispatched: code != ErrorCode.Unauthenticated || state.DataAuthority);
        await PrivateAsync(JsonDefaults.Serialize(reply));
    }

    private static async Task SdkAsync<T>(ManagedCode.Communication.Result<T> result, ErrorCode code)
    {
        await SagaTimeoutRf3Assertions.AssertSdkErrorAsync(result, code);
        await Assert.That(result.Value).IsNull();
        await PrivateAsync(JsonDefaults.Serialize(result));
    }

    private static async Task PrivateAsync(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        await Assert.That(text.Contains(EventMessageSensitivePublicProtocol.BodyCanary, StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Contains(EventMessageSensitivePublicProtocol.HeaderCanary, StringComparison.Ordinal)).IsFalse();
    }
}
