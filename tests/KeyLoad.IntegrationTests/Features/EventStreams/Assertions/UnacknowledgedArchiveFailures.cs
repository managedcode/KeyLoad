using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class UnacknowledgedArchiveFailures
{
    internal static async Task RequireAsync<T>(RequestCqrsRf3Callers callers, UnacknowledgedArchiveState state,
        string tool, object request, Func<Task<Result<T>>> sdk, ErrorCode code,
        string detail, string safeDetail, CancellationToken token)
    {
        var result = await sdk();
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Value).IsNull();
        await SqlRf3Protocol.EqualAsync(Errors.Problem(code, detail), result.Problem);
        await UnacknowledgedArchiveCurrent.RunAsync(callers, state, token);
        var reply = await callers.Mcp.CallAsync(tool, request, token);
        await McpCallerAssertions.ErrorAsync(reply, code, dispatched: true);
        var expected = JsonSerializer.SerializeToElement(Errors.Problem(code, safeDetail), JsonDefaults.Options);
        await Assert.That(JsonElement.DeepEquals(expected,
            reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error))).IsTrue();
        await UnacknowledgedArchiveCurrent.RunAsync(callers, state, token);
    }

    internal static Task SettledReceiveAsync(RequestCqrsRf3Callers callers, UnacknowledgedArchiveState state,
        CancellationToken token)
        => RequireAsync(callers, state, McpCallerTools.SubscriptionsReceive, state.Receive,
            () => callers.Sdk.ReceiveSubscriptionAsync(state.Receive, token), ErrorCode.StaleLease,
            UnacknowledgedArchiveProtocol.StaleReceive, UnacknowledgedArchiveProtocol.SafeFailure, token);
}
