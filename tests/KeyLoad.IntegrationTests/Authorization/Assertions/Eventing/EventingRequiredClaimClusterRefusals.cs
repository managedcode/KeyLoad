using System.Text.Json;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class EventingRequiredClaimClusterRefusals
{
    internal static async Task ReceiveAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        EventingRequiredClaimClusterState state, ReceiveRequest request, ErrorCode code, string detail, CancellationToken token)
    {
        var input = state.Input;
        var sql = SqlClusterProtocol.Call(input.Partition, McpCallerTools.MessagesReceive, request);
        await EventingRequiredClaimClusterCurrent.ObserveAsync(root, state, async () =>
            await SdkAsync(await reader.Sdk.ReceiveAsync(request, token), code, detail), token);
        await EventingRequiredClaimClusterCurrent.ObserveAsync(root, state, () => McpAsync(
            () => reader.Mcp.CallAsync(McpCallerTools.MessagesReceive, request, token), state, code), token);
        await EventingRequiredClaimClusterCurrent.ObserveAsync(root, state, async () =>
            await SqlAsync(await reader.Sdk.ExecuteSqlAsync(sql, token), code, detail), token);
        await EventingRequiredClaimClusterCurrent.ObserveAsync(root, state, () => McpAsync(
            () => reader.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token), state, code), token);
    }
    internal static async Task SdkAsync<T>(Result<T> actual, ErrorCode code, string detail)
    {
        await Assert.That(actual.IsFailed).IsTrue();
        await McpCallerAssertions.NoValueAsync(actual);
        await SqlClusterProtocol.EqualAsync(Errors.Problem(code, detail), actual.Problem);
    }
    internal static async Task SqlAsync(Result<JsonElement> actual, ErrorCode code, string detail)
    {
        await Assert.That(actual.IsFailed).IsTrue();
        await Assert.That(actual.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await SqlClusterProtocol.EqualAsync(Errors.Problem(code, detail), actual.Problem);
    }
    internal static async Task McpAsync(Func<Task<CallToolResult>> call, EventingRequiredClaimClusterState state, ErrorCode code, string? permissionDetail = null)
    {
        if (code == ErrorCode.Unauthenticated)
        {
            var failure = await Assert.ThrowsAsync<HttpRequestException>(() => call());
            await Assert.That(failure!.StatusCode).IsEqualTo(System.Net.HttpStatusCode.Unauthorized);
            await Assert.That(failure.Message.Contains(state.Input.Key, StringComparison.Ordinal)).IsFalse();
            await Assert.That(failure.Message.Contains("PROCESSING-CANARY", StringComparison.Ordinal)).IsFalse();
            return;
        }
        var reply = await call();
        _ = (await McpCallerAssertions.ErrorAsync(reply, code, dispatched: true)).Value;
        var detail = code == ErrorCode.PermissionDenied ? permissionDetail ?? "The authenticated principal is not permitted to perform this operation."
            : "The database operation could not be completed.";
        var expected = JsonSerializer.SerializeToElement(Errors.Problem(code, detail), JsonDefaults.Options);
        await Assert.That(JsonElement.DeepEquals(expected, reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error))).IsTrue();
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Input.Key,
            state.Input.Header ? "HEADER-PROCESSING-CANARY" : "BODY-PROCESSING-CANARY");
    }
}
