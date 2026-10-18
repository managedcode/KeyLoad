using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphLabelUseClusterReads
{
    internal static async Task RunAsync(RequestCqrsClusterCallers reader, GraphLabelUseClusterState state,
        bool allow, CancellationToken token)
    {
        foreach (var labels in new string[]?[] { null, [] })
        {
            await HealthyAsync(reader, GraphLabelUseClusterProtocol.Request(state.Seed.Partition, labels),
                GraphLabelUseClusterProtocol.Expected(state.Seed.Partition, labels), token);
        }
        foreach (var labels in new[] { new[] { "route" }, new[] { "unmatched" } })
        {
            var request = GraphLabelUseClusterProtocol.Request(state.Seed.Partition, labels);
            if (allow)
            {
                await HealthyAsync(reader, request, GraphLabelUseClusterProtocol.Expected(state.Seed.Partition, labels), token);
            }
            else
            {
                await RefuseAsync(reader, request, state, ErrorCode.PermissionDenied,
                    GraphLabelUseClusterProtocol.SdkDenied, token);
            }
        }
        await RefuseAsync(reader, GraphLabelUseClusterProtocol.Request(state.Seed.Partition, ["route"], true), state,
            allow ? ErrorCode.NotFound : ErrorCode.PermissionDenied,
            allow ? "The graph vertex is unavailable." : GraphLabelUseClusterProtocol.SdkDenied, token);
    }

    private static async Task HealthyAsync(RequestCqrsClusterCallers caller, TraverseRequest request,
        global::KeyLoad.GraphTraversal expected, CancellationToken token)
    {
        var sql = SqlClusterProtocol.Call(request.Partition, McpCallerTools.GraphTraverse, request);
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.TraverseAsync(request, token)));
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<global::KeyLoad.GraphTraversal>(
            await caller.Mcp.CallAsync(McpCallerTools.GraphTraverse, request, token))).Value);
        await SqlClusterProtocol.EqualAsync(expected, await SqlClusterProtocol.SdkAsync<global::KeyLoad.GraphTraversal>(caller.Sdk, sql, token));
        await SqlClusterProtocol.EqualAsync(expected, await SqlClusterProtocol.McpAsync<global::KeyLoad.GraphTraversal>(caller.Mcp, sql, token));
    }

    private static async Task RefuseAsync(RequestCqrsClusterCallers caller, TraverseRequest request, GraphLabelUseClusterState state,
        ErrorCode code, string detail, CancellationToken token)
    {
        var sql = SqlClusterProtocol.Call(request.Partition, McpCallerTools.GraphTraverse, request);
        var direct = await caller.Sdk.TraverseAsync(request, token);
        await Assert.That(direct.Value).IsNull();
        await Assert.That(direct.IsFailed).IsTrue();
        await SqlClusterProtocol.EqualAsync(Errors.Problem(code, detail), direct.Problem);
        var compiled = await caller.Sdk.ExecuteSqlAsync(sql, token);
        await Assert.That(compiled.Value.ValueKind).IsEqualTo(System.Text.Json.JsonValueKind.Undefined);
        await Assert.That(compiled.IsFailed).IsTrue();
        await SqlClusterProtocol.EqualAsync(Errors.Problem(code, detail), compiled.Problem);
        await OfficialErrorAsync(await caller.Mcp.CallAsync(McpCallerTools.GraphTraverse, request, token), state, code);
        await OfficialErrorAsync(await caller.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token), state, code);
    }

    private static async Task OfficialErrorAsync(global::ModelContextProtocol.Protocol.CallToolResult reply,
        GraphLabelUseClusterState state, ErrorCode code)
    {
        await McpCallerAssertions.ErrorAsync(reply, code, dispatched: true);
        if (code == ErrorCode.PermissionDenied)
        {
            await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
                .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(GraphLabelUseClusterProtocol.McpDenied);
        }
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Reader.Secret, "attribute-value");
    }
}
