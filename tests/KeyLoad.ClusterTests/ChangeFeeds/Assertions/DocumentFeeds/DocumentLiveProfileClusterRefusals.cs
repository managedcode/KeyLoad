using System.Text.Json;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;
using KeyLoad.Query;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;

namespace KeyLoad.ClusterTests.ChangeFeeds;

internal static class DocumentLiveProfileClusterRefusals
{
    internal static async Task BothAsync(RequestCqrsClusterCallers reader, DocumentLiveProfileClusterState state,
        AstQueryRequest query, string cursor, ErrorCode code, string detail, CancellationToken token)
    {
        var start = new StartLiveQueryRequest(query);
        var read = new ReadLiveQueryRequest(query, cursor);
        await SdkAsync(await reader.Sdk.StartLiveQueryAsync(start, token), code, detail);
        await SdkAsync(await reader.Sdk.ReadLiveQueryAsync(read, token), code, detail);
        await McpAsync(await reader.Mcp.CallAsync(FeedLiveClusterProtocol.LiveStart, start, token), code, detail, state.Key);
        await McpAsync(await reader.Mcp.CallAsync(FeedLiveClusterProtocol.LiveRead, read, token), code, detail, state.Key);
        var startSql = SqlClusterProtocol.Call(state.Partition, FeedLiveClusterProtocol.LiveStart, start);
        var readSql = SqlClusterProtocol.Call(state.Partition, FeedLiveClusterProtocol.LiveRead, read);
        await SqlAsync(await reader.Sdk.ExecuteSqlAsync(startSql, token), code, detail);
        await SqlAsync(await reader.Sdk.ExecuteSqlAsync(readSql, token), code, detail);
        await McpAsync(await reader.Mcp.CallAsync(SqlOperationProtocol.ToolName, startSql, token), code, detail, state.Key);
        await McpAsync(await reader.Mcp.CallAsync(SqlOperationProtocol.ToolName, readSql, token), code, detail, state.Key);
    }
    internal static async Task ReadAsync(RequestCqrsClusterCallers reader, DocumentLiveProfileClusterState state,
        ReadLiveQueryRequest request, ErrorCode code, string detail, CancellationToken token)
    {
        await SdkAsync(await reader.Sdk.ReadLiveQueryAsync(request, token), code, detail);
        await McpAsync(await reader.Mcp.CallAsync(FeedLiveClusterProtocol.LiveRead, request, token), code, detail, state.Key);
        var sql = SqlClusterProtocol.Call(state.Partition, FeedLiveClusterProtocol.LiveRead, request);
        await SqlAsync(await reader.Sdk.ExecuteSqlAsync(sql, token), code, detail);
        await McpAsync(await reader.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token), code, detail, state.Key);
    }
    internal static async Task UnauthenticatedAsync(RequestCqrsClusterCallers reader, DocumentLiveProfileClusterState state, CancellationToken token)
    {
        await SdkAsync(await reader.Sdk.StartLiveQueryAsync(new(state.Query), token), ErrorCode.Unauthenticated, "The credential is unavailable or expired.");
        await SdkAsync(await reader.Sdk.ReadLiveQueryAsync(new(state.Query, state.OriginalCursor), token), ErrorCode.Unauthenticated, "The credential is unavailable or expired.");
        var startSql = SqlClusterProtocol.Call(state.Partition, FeedLiveClusterProtocol.LiveStart, new StartLiveQueryRequest(state.Query));
        var readSql = SqlClusterProtocol.Call(state.Partition, FeedLiveClusterProtocol.LiveRead, new ReadLiveQueryRequest(state.Query, state.OriginalCursor));
        await SqlAsync(await reader.Sdk.ExecuteSqlAsync(startSql, token), ErrorCode.Unauthenticated, "The credential is unavailable or expired.");
        await SqlAsync(await reader.Sdk.ExecuteSqlAsync(readSql, token), ErrorCode.Unauthenticated, "The credential is unavailable or expired.");
        foreach (var tool in new[] { FeedLiveClusterProtocol.LiveStart, FeedLiveClusterProtocol.LiveRead })
        {
            object request = tool == FeedLiveClusterProtocol.LiveStart ? new StartLiveQueryRequest(state.Query) : new ReadLiveQueryRequest(state.Query, state.OriginalCursor);
            var failure = await Assert.ThrowsAsync<HttpRequestException>(() => reader.Mcp.CallAsync(tool, request, token));
            await Assert.That(failure!.StatusCode).IsEqualTo(System.Net.HttpStatusCode.Unauthorized);
            await Assert.That(failure.Message.Contains(DocumentLiveProfileClusterState.Secret, StringComparison.Ordinal)).IsFalse();
            var sql = tool == FeedLiveClusterProtocol.LiveStart ? startSql : readSql;
            var sqlFailure = await Assert.ThrowsAsync<HttpRequestException>(() => reader.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token));
            await Assert.That(sqlFailure!.StatusCode).IsEqualTo(System.Net.HttpStatusCode.Unauthorized);
        }
    }
    internal static async Task SdkAsync<T>(Result<T> result, ErrorCode code, string detail)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await McpCallerAssertions.NoValueAsync(result);
        await SqlClusterProtocol.EqualAsync(Errors.Problem(code, detail), result.Problem);
    }
    internal static async Task SqlAsync(Result<JsonElement> result, ErrorCode code, string detail)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await SqlClusterProtocol.EqualAsync(Errors.Problem(code, detail), result.Problem);
    }
    private static async Task McpAsync(CallToolResult reply, ErrorCode code, string detail, string credential)
    {
        _ = (await McpCallerAssertions.ErrorAsync(reply, code, dispatched: true)).Value;
        var safe = code == ErrorCode.PermissionDenied ? detail : code == ErrorCode.BudgetExceeded
            ? "The operation exceeds the available resource capacity." : "The database operation could not be completed.";
        var expected = JsonSerializer.SerializeToElement(Errors.Problem(code, safe), JsonDefaults.Options);
        await Assert.That(JsonElement.DeepEquals(expected, reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error))).IsTrue();
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, DocumentLiveProfileClusterState.Secret);
    }
}
