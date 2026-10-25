using System.Text.Json;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class RequiredProcessingPolicyClusterRefusals
{
    internal static async Task AllAsync(RequestCqrsClusterCallers reader, RequiredProcessingPolicyClusterState state,
        ProcessingRequest request, string detail, CancellationToken token)
    {
        await SdkAsync(await reader.Sdk.CommitProcessingAsync(request, token), ErrorCode.PermissionDenied, detail);
        await McpAsync(await reader.Mcp.CallAsync(RequiredProcessingPolicyClusterState.Tool, request, token), state, ErrorCode.PermissionDenied);
        var sql = SqlClusterProtocol.Call(state.Partition, RequiredProcessingPolicyClusterState.Tool, request);
        var sdk = await reader.Sdk.ExecuteSqlAsync(sql, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.PermissionDenied, detail), sdk.Problem);
        await McpAsync(await reader.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token), state, ErrorCode.PermissionDenied);
    }
    internal static async Task RevokedAsync(RequestCqrsClusterCallers reader, RequiredProcessingPolicyClusterState state, CancellationToken token)
    {
        await SdkAsync(await reader.Sdk.CommitProcessingAsync(state.Original, token), ErrorCode.Unauthenticated, "The credential is unavailable or expired.");
        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => reader.Mcp.CallAsync(RequiredProcessingPolicyClusterState.Tool, state.Original, token));
        await Assert.That(failure!.StatusCode).IsEqualTo(System.Net.HttpStatusCode.Unauthorized);
        var sql = SqlClusterProtocol.Call(state.Partition, RequiredProcessingPolicyClusterState.Tool, state.Original);
        var sdk = await reader.Sdk.ExecuteSqlAsync(sql, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.Unauthenticated, "The credential is unavailable or expired."), sdk.Problem);
        var officialSql = await Assert.ThrowsAsync<HttpRequestException>(() => reader.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token));
        await Assert.That(officialSql!.StatusCode).IsEqualTo(System.Net.HttpStatusCode.Unauthorized);
    }
    internal static async Task CancelledAsync(RequestCqrsClusterCallers reader, RequiredProcessingPolicyClusterState state, CancellationToken token)
    {
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token);
        await cancelled.CancelAsync();
        const string detail = "The write response is unavailable. Retry the same command ID.";
        await SdkAsync(await reader.Sdk.CommitProcessingAsync(state.Healthy, cancelled.Token), ErrorCode.UnknownWriteOutcome, detail);
        var sql = SqlClusterProtocol.Call(state.Partition, RequiredProcessingPolicyClusterState.Tool, state.Healthy);
        var sdk = await reader.Sdk.ExecuteSqlAsync(sql, cancelled.Token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.UnknownWriteOutcome, detail), sdk.Problem);
        var official = await Assert.ThrowsAsync<OperationCanceledException>(() => reader.Mcp.CallAsync(RequiredProcessingPolicyClusterState.Tool, state.Healthy, cancelled.Token));
        var officialSql = await Assert.ThrowsAsync<OperationCanceledException>(() => reader.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, cancelled.Token));
        await Assert.That(official!.CancellationToken).IsEqualTo(cancelled.Token);
        await Assert.That(officialSql!.CancellationToken).IsEqualTo(cancelled.Token);
    }
    private static async Task SdkAsync(Result<CommitReceipt> actual, ErrorCode code, string detail)
    {
        await Assert.That(actual.IsFailed).IsTrue();
        await Assert.That(actual.Value).IsNull();
        await SqlClusterProtocol.EqualAsync(Errors.Problem(code, detail), actual.Problem);
    }
    private static async Task McpAsync(CallToolResult actual, RequiredProcessingPolicyClusterState state, ErrorCode code)
    {
        _ = (await McpCallerAssertions.ErrorAsync(actual, code, dispatched: true)).Value;
        var expected = JsonSerializer.SerializeToElement(Errors.Problem(code, "The authenticated principal is not permitted to perform this operation."), JsonDefaults.Options);
        await Assert.That(JsonElement.DeepEquals(expected, actual.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error))).IsTrue();
        await McpCallerAssertions.DoesNotDiscloseAsync(actual, state.Key, state.Header ? "HEADER-PROCESSING-CANARY" : "BODY-PROCESSING-CANARY");
    }
}
