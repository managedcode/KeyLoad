using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.GraphTraversal;

internal static class GraphPathRebindR44Refusals
{
    private const string DetailKey = "detail";
    private const string RevisionDetail = "The expected revision does not match.";
    private const string McpRevisionDetail = "The database operation could not be completed.";

    internal static async Task StaleAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
        GraphPathRebindR44State state, CancellationToken token)
    {
        var sdk = await admin.Sdk.CommitAsync(state.Stale, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value).IsNull();
        await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.RevisionConflict, RevisionDetail), sdk.Problem);
        await GraphPathRebindR44Reads.MatrixAsync(admin, reader, state, token);
        var reply = await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Stale, token);
        _ = (await McpCallerAssertions.ErrorAsync(reply, ErrorCode.RevisionConflict, dispatched: true)).Value;
        await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(DetailKey).GetString()).IsEqualTo(McpRevisionDetail);
        await GraphPathRebindR44Reads.MatrixAsync(admin, reader, state, token);
    }

    internal static async Task BoundsAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
        GraphPathRebindR44State state, CancellationToken token)
    {
        foreach (var request in new[] { state.Request(state.T) with { MaxVertices = state.VertexCap - 1 },
            state.Request(state.T) with { MaxEdges = state.EdgeCap - 1 } })
        {
            var result = await reader.Sdk.ShortestPathAsync(request, token);
            await Assert.That(result.IsFailed).IsTrue();
            await Assert.That(result.Value).IsNull();
            await Assert.That(result.Problem!.ErrorCode).IsEqualTo(ErrorCode.BudgetExceeded.ToString());
            await GraphPathRebindR44Reads.MatrixAsync(admin, reader, state, token);
            _ = (await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(GraphPathClusterScenario.DirectTool, request, token),
                ErrorCode.BudgetExceeded, dispatched: true)).Value;
            await GraphPathRebindR44Reads.MatrixAsync(admin, reader, state, token);
        }
    }
}
