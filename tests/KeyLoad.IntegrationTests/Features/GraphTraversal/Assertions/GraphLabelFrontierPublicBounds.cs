using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphLabelFrontierPublicBounds
{
    private const string DetailKey = "detail";
    private const string EdgeDetail = "The edge visit budget was exhausted.";
    private const string VertexDetail = "The vertex visit budget was exhausted.";
    private const string SafeDetail = "The operation exceeds the available resource capacity.";

    internal static async Task VerifyAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers reader,
        GraphLabelFrontierPublicState state, CancellationToken token)
    {
        await GraphLabelFrontierPublicReads.MatrixAsync(admin, reader, state, token);
        foreach (var profile in new[] { 0, 2, 4, 5 })
        {
            var count = state.Expected(3, profile).Vertices.Length;
            await RefuseAsync(admin, reader, state, state.Request(3, profile, count,
                state.Candidates(3, profile) - 1), EdgeDetail, token);
            if (count > 1)
            {
                await RefuseAsync(admin, reader, state, state.Request(3, profile, count - 1,
                    state.Candidates(3, profile)), VertexDetail, token);
            }
        }
    }

    private static async Task RefuseAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers reader,
        GraphLabelFrontierPublicState state, TraverseRequest request, string detail, CancellationToken token)
    {
        var result = await reader.Sdk.TraverseAsync(request, token);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.BudgetExceeded, detail), result.Problem);
        await GraphLabelFrontierPublicReads.MatrixAsync(admin, reader, state, token);
        var reply = await reader.Mcp.CallAsync(McpCallerTools.GraphTraverse, request, token);
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.BudgetExceeded, dispatched: true);
        await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(DetailKey).GetString()).IsEqualTo(SafeDetail);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Seed.Reader.Secret, GraphIncomingRf3Scenario.PrivateMarker);
        await GraphLabelFrontierPublicReads.MatrixAsync(admin, reader, state, token);
    }
}
