using System.Text;
using System.Text.Json;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.Messaging;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;
using ModelContextProtocol.Protocol;
namespace KeyLoad.IntegrationTests.Authorization;

internal static class EventMessageSensitiveSafeR52Refusals
{
    internal static async Task ReceiveAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers caller,
        EventMessageSensitiveSafeR52State state, object request, ErrorCode code, CancellationToken token)
    {
        var tool = state.Model.Subscription ? McpCallerTools.SubscriptionsReceive : McpCallerTools.MessagesReceive;
        var detail = code == ErrorCode.Unauthenticated ? EventMessageSensitiveSafeR52Protocol.UnavailableDetail
            : state.Model.Subscription && state.Model.DataAuthority ? EventMessageSensitiveSafeR52Protocol.SubscriptionChangedDetail
                : EventMessageSensitiveSafeR52Protocol.ChangedPolicyDetail;
        if (request is ReceiveSubscriptionRequest subscription)
        {
            await SdkAsync(await caller.Sdk.ReceiveSubscriptionAsync(subscription, token), code, detail, state);
        }
        else
        {
            await SdkAsync(await caller.Sdk.ReceiveAsync((ReceiveRequest)request, token), code, detail, state);
        }
        await EventMessageSensitiveSafeR52Current.RequireAsync(root, state, token);
        await McpAsync(caller, state, tool, request, code, token);
        await EventMessageSensitiveSafeR52Current.RequireAsync(root, state, token);
        var sql = SqlClusterProtocol.Call(state.Model.Partition, tool, request);
        await SqlAsync(await caller.Sdk.ExecuteSqlAsync(sql, token), code, detail, state);
        await EventMessageSensitiveSafeR52Current.RequireAsync(root, state, token);
        await McpAsync(caller, state, SqlOperationProtocol.ToolName, sql, code, token);
        await EventMessageSensitiveSafeR52Current.RequireAsync(root, state, token);
    }

    private static async Task McpAsync(RequestCqrsClusterCallers caller, EventMessageSensitiveSafeR52State state,
        string tool, object request, ErrorCode code, CancellationToken token)
    {
        if (code == ErrorCode.Unauthenticated && !state.Model.DataAuthority)
        {
            var failure = await Assert.ThrowsAsync<HttpRequestException>(() => caller.Mcp.CallAsync(tool, request, token));
            await Assert.That(failure!.StatusCode).IsEqualTo(System.Net.HttpStatusCode.Unauthorized);
            await PrivateAsync(Encoding.UTF8.GetBytes(failure.Message), state);
            return;
        }
        var reply = await caller.Mcp.CallAsync(tool, request, token);
        _ = (await McpCallerAssertions.ErrorAsync(reply, code, dispatched: true)).Value;
        await McpProblemAsync(reply, code, state);
    }

    private static async Task SdkAsync<T>(ManagedCode.Communication.Result<T> result, ErrorCode code,
        string detail, EventMessageSensitiveSafeR52State state)
    {
        await PrivateAsync(JsonDefaults.Serialize(result), state);
        await SagaTimeoutClusterAssertions.AssertSdkErrorAsync(result, code);
        await McpCallerAssertions.NoValueAsync(result);
        await SqlClusterProtocol.EqualAsync(Errors.Problem(code, detail), result.Problem);
    }

    private static async Task SqlAsync(ManagedCode.Communication.Result<JsonElement> result, ErrorCode code,
        string detail, EventMessageSensitiveSafeR52State state)
    {
        await PrivateAsync(JsonDefaults.Serialize(result), state);
        await SagaTimeoutClusterAssertions.AssertSdkErrorAsync(result, code);
        await Assert.That(result.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await SqlClusterProtocol.EqualAsync(Errors.Problem(code, detail), result.Problem);
    }

    private static async Task McpProblemAsync(CallToolResult reply, ErrorCode code, EventMessageSensitiveSafeR52State state)
    {
        await PrivateAsync(JsonDefaults.Serialize(reply), state);
        var detail = code == ErrorCode.Unauthenticated ? EventMessageSensitiveSafeR52Protocol.McpUnavailableDetail
            : EventMessageSensitiveSafeR52Protocol.McpDeniedDetail;
        var expected = JsonSerializer.SerializeToElement(Errors.Problem(code, detail), JsonDefaults.Options);
        var actual = reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error);
        await Assert.That(JsonElement.DeepEquals(expected, actual)).IsTrue();
    }

    private static async Task PrivateAsync(byte[] bytes, EventMessageSensitiveSafeR52State state)
    {
        var text = Encoding.UTF8.GetString(bytes);
        await Assert.That(text.Contains(EventMessageSensitiveSafeR52Protocol.Payload, StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Contains(EventMessageSensitiveSafeR52Protocol.Headers, StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Contains(EventMessageSensitiveSafeR52Protocol.Canary, StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Contains(state.Model.Key, StringComparison.Ordinal)).IsFalse();
    }
}
