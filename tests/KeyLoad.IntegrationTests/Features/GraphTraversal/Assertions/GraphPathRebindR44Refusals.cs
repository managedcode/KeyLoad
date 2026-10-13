using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphPathRebindR44Refusals
{
    private const string DetailKey = "detail";
    private const string RevisionDetail = "The expected revision does not match.";
    private const string McpRevisionDetail = "The database operation could not be completed.";

    internal static async Task StaleAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers reader,
        GraphPathRebindR44State state, CancellationToken token)
    {
        var sdk = await admin.Sdk.CommitAsync(state.Stale, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value).IsNull();
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.RevisionConflict, RevisionDetail), sdk.Problem);
        await GraphPathRebindR44Reads.MatrixAsync(admin, reader, state, token);
        var reply = await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Stale, token);
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.RevisionConflict, dispatched: true);
        await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(DetailKey).GetString()).IsEqualTo(McpRevisionDetail);
        await GraphPathRebindR44Reads.MatrixAsync(admin, reader, state, token);
    }

    internal static async Task BoundsAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers reader,
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
            await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(GraphPathRf3Scenario.DirectTool, request, token),
                ErrorCode.BudgetExceeded, dispatched: true);
            await GraphPathRebindR44Reads.MatrixAsync(admin, reader, state, token);
        }
    }
}
