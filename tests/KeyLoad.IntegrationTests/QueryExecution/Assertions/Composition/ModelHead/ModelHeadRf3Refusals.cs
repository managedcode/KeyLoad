using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class ModelHeadRf3Refusals
{
    internal static async Task ReadAsync(RequestCqrsRf3Callers reader, ModelHeadRf3State state,
        bool queue, long generation, ErrorCode code, string detail, CancellationToken token)
    {
        var sql = ModelHeadRf3Requests.Sql(state, queue, generation);
        var ast = ModelHeadRf3Requests.Ast(state, queue, generation);
        await SdkAsync(await reader.Sdk.QueryAsync(sql, token), code, detail);
        await SdkAsync(await reader.Sdk.QueryAstAsync(ast, token), code, detail);
        await McpAsync(await reader.Mcp.CallAsync(McpCallerTools.QueryExecute, sql, token), state, code, true);
        await McpAsync(await reader.Mcp.CallAsync(McpCallerTools.QueryAst, ast, token), state, code, true);
        var request = new SqlOperationRequest(state.Partition, sql.Sql, AllowFullScan: true);
        await SqlAsync(await reader.Sdk.ExecuteSqlAsync(request, token), code, detail);
        await McpAsync(await reader.Mcp.CallAsync(SqlOperationProtocol.ToolName, request, token), state, code, true);
        if (code == ErrorCode.PermissionDenied)
        {
            await TypedAsync(reader, state, queue, code, detail, token);
        }
    }
    private static async Task TypedAsync(RequestCqrsRf3Callers reader, ModelHeadRf3State state,
        bool queue, ErrorCode code, string detail, CancellationToken token)
    {
        if (queue)
        {
            await SdkAsync(await reader.Sdk.InspectAsync(new(state.Lane, "ready"), token), code, detail);
        }
        else
        {
            await SdkAsync(await reader.Sdk.ReadStreamAsync(new(state.Source, Limit: 10), token), code, detail);
        }
    }
    internal static async Task UpdateAsync(RequestCqrsRf3Callers reader, ModelHeadRf3State state, CancellationToken token)
    {
        var request = new SqlOperationRequest(state.Partition, "UPDATE [model-head-queue] SET state = 'Acked'");
        await SqlAsync(await reader.Sdk.ExecuteSqlAsync(request, token), ErrorCode.UnsupportedCapability,
            "The unified SQL version, statement or target operation is unsupported.");
        await McpAsync(await reader.Mcp.CallAsync(SqlOperationProtocol.ToolName, request, token), state, ErrorCode.UnsupportedCapability, false);
    }
    private static async Task SdkAsync<T>(Result<T> result, ErrorCode code, string detail)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await SqlRf3Protocol.EqualAsync(Errors.Problem(code, detail), result.Problem);
    }
    private static async Task SqlAsync(Result<JsonElement> result, ErrorCode code, string detail)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await SqlRf3Protocol.EqualAsync(Errors.Problem(code, detail), result.Problem);
    }
    private static async Task McpAsync(CallToolResult reply, ModelHeadRf3State state, ErrorCode code, bool dispatched)
    {
        await McpCallerAssertions.ErrorAsync(reply, code, dispatched);
        var detail = code == ErrorCode.PermissionDenied ? "The principal cannot perform this operation in this scope."
            : "The database operation could not be completed.";
        var expected = JsonSerializer.SerializeToElement(Errors.Problem(code, detail), JsonDefaults.Options);
        await Assert.That(JsonElement.DeepEquals(expected, reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error))).IsTrue();
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Identity.Secret, ModelHeadRf3State.Secret);
    }
}
