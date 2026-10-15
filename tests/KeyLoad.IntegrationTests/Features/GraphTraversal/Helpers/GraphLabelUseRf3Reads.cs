using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphLabelUseRf3Reads
{
    internal static async Task RunAsync(RequestCqrsRf3Callers reader, GraphLabelUseRf3State state,
        bool allow, CancellationToken token)
    {
        foreach (var labels in new string[]?[] { null, [] })
        {
            await HealthyAsync(reader, GraphLabelUseRf3Protocol.Request(state.Seed.Partition, labels),
                GraphLabelUseRf3Protocol.Expected(state.Seed.Partition, labels), token);
        }
        foreach (var labels in new[] { new[] { "route" }, new[] { "unmatched" } })
        {
            var request = GraphLabelUseRf3Protocol.Request(state.Seed.Partition, labels);
            if (allow)
            {
                await HealthyAsync(reader, request, GraphLabelUseRf3Protocol.Expected(state.Seed.Partition, labels), token);
            }
            else
            {
                await RefuseAsync(reader, request, state, ErrorCode.PermissionDenied,
                    GraphLabelUseRf3Protocol.SdkDenied, token);
            }
        }
        await RefuseAsync(reader, GraphLabelUseRf3Protocol.Request(state.Seed.Partition, ["route"], true), state,
            allow ? ErrorCode.NotFound : ErrorCode.PermissionDenied,
            allow ? "The graph vertex is unavailable." : GraphLabelUseRf3Protocol.SdkDenied, token);
    }

    private static async Task HealthyAsync(RequestCqrsRf3Callers caller, TraverseRequest request,
        global::KeyLoad.GraphTraversal expected, CancellationToken token)
    {
        var sql = SqlRf3Protocol.Call(request.Partition, McpCallerTools.GraphTraverse, request);
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.TraverseAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<global::KeyLoad.GraphTraversal>(
            await caller.Mcp.CallAsync(McpCallerTools.GraphTraverse, request, token))).Value);
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.SdkAsync<global::KeyLoad.GraphTraversal>(caller.Sdk, sql, token));
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.McpAsync<global::KeyLoad.GraphTraversal>(caller.Mcp, sql, token));
    }

    private static async Task RefuseAsync(RequestCqrsRf3Callers caller, TraverseRequest request, GraphLabelUseRf3State state,
        ErrorCode code, string detail, CancellationToken token)
    {
        var sql = SqlRf3Protocol.Call(request.Partition, McpCallerTools.GraphTraverse, request);
        var direct = await caller.Sdk.TraverseAsync(request, token);
        await Assert.That(direct.Value).IsNull();
        await Assert.That(direct.IsFailed).IsTrue();
        await SqlRf3Protocol.EqualAsync(Errors.Problem(code, detail), direct.Problem);
        var compiled = await caller.Sdk.ExecuteSqlAsync(sql, token);
        await Assert.That(compiled.Value.ValueKind).IsEqualTo(System.Text.Json.JsonValueKind.Undefined);
        await Assert.That(compiled.IsFailed).IsTrue();
        await SqlRf3Protocol.EqualAsync(Errors.Problem(code, detail), compiled.Problem);
        await OfficialErrorAsync(await caller.Mcp.CallAsync(McpCallerTools.GraphTraverse, request, token), state, code);
        await OfficialErrorAsync(await caller.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token), state, code);
    }

    private static async Task OfficialErrorAsync(global::ModelContextProtocol.Protocol.CallToolResult reply,
        GraphLabelUseRf3State state, ErrorCode code)
    {
        await McpCallerAssertions.ErrorAsync(reply, code, dispatched: true);
        if (code == ErrorCode.PermissionDenied)
        {
            await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
                .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(GraphLabelUseRf3Protocol.McpDenied);
        }
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Reader.Secret, "attribute-value");
    }
}
