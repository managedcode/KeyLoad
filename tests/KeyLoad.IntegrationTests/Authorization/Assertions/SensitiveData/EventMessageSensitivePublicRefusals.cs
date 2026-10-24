using System.Text;
using System.Text.Json;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.Messaging;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class EventMessageSensitivePublicRefusals
{
    internal static Task ReceiveAsync(RequestCqrsClusterCallers callers, EventMessageSensitivePublicState state,
        object request, ErrorCode code, CancellationToken token)
        => request is ReceiveSubscriptionRequest subscription
            ? AllAsync(callers, state, McpCallerTools.SubscriptionsReceive, subscription,
                () => callers.Sdk.ReceiveSubscriptionAsync(subscription, token), code, token)
            : AllAsync(callers, state, McpCallerTools.MessagesReceive, (ReceiveRequest)request,
                () => callers.Sdk.ReceiveAsync((ReceiveRequest)request, token), code, token);

    private static async Task AllAsync<T>(RequestCqrsClusterCallers callers, EventMessageSensitivePublicState state,
        string tool, object request, Func<Task<ManagedCode.Communication.Result<T>>> sdk, ErrorCode code, CancellationToken token)
    {
        await SdkAsync(await sdk(), code);
        await McpAsync(callers, state, tool, request, code, token);
        var sql = KeyLoad.IntegrationTests.QueryExecution.SqlClusterProtocol.Call(state.Partition, tool, request);
        await SdkAsync(await callers.Sdk.ExecuteSqlAsync(sql, token), code);
        await McpAsync(callers, state, SqlOperationProtocol.ToolName, sql, code, token);
    }

    private static async Task McpAsync(RequestCqrsClusterCallers callers, EventMessageSensitivePublicState state,
        string tool, object request, ErrorCode code, CancellationToken token)
    {
        if (code == ErrorCode.Unauthenticated && !state.DataAuthority)
        {
            var failure = await Assert.ThrowsAsync<HttpRequestException>(() => callers.Mcp.CallAsync(tool, request, token));
            await Assert.That(failure!.StatusCode).IsEqualTo(System.Net.HttpStatusCode.Unauthorized);
            await PrivateAsync(Encoding.UTF8.GetBytes(failure.Message));
            await Assert.That(failure.Message.Contains(state.Key, StringComparison.Ordinal)).IsFalse();
            return;
        }
        var reply = await callers.Mcp.CallAsync(tool, request, token);
        await McpCallerAssertions.ErrorAsync(reply, code, dispatched: true);
        await PrivateAsync(JsonDefaults.Serialize(reply));
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Key,
            EventMessageSensitivePublicProtocol.BodyCanary);
    }

    private static async Task SdkAsync<T>(ManagedCode.Communication.Result<T> result, ErrorCode code)
    {
        await SagaTimeoutClusterAssertions.AssertSdkErrorAsync(result, code);
        if (result.Value is JsonElement value)
        {
            await Assert.That(value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        }
        else
        {
            await Assert.That(result.Value).IsNull();
        }
        await PrivateAsync(JsonDefaults.Serialize(result));
    }

    private static async Task PrivateAsync(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        await Assert.That(text.Contains(EventMessageSensitivePublicProtocol.BodyCanary, StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Contains(EventMessageSensitivePublicProtocol.HeaderCanary, StringComparison.Ordinal)).IsFalse();
    }
}
