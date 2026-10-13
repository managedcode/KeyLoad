using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphCycleFrontierPublicBounds
{
    internal static async Task VerifyAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers reader,
        GraphCycleFrontierPublicState state, CancellationToken token)
    {
        await GraphCycleFrontierPublicReads.MatrixAsync(admin, reader, state, token);
        foreach (var request in new[] { state.Request(3, state.Hidden ? 2 : 3, state.Hidden ? 5 : 7),
            state.Request(3, state.Hidden ? 3 : 4, state.Hidden ? 4 : 6), state.Request(1, 3, 2) })
        {
            var sdk = await reader.Sdk.TraverseAsync(request, token);
            await Assert.That(sdk.IsSuccess).IsFalse();
            await Assert.That(sdk.Problem!.ErrorCode).IsEqualTo(ErrorCode.BudgetExceeded.ToString());
            await Assert.That(sdk.Value).IsNull();
            await GraphCycleFrontierPublicReads.MatrixAsync(admin, reader, state, token);
            await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(McpCallerTools.GraphTraverse, request, token),
                ErrorCode.BudgetExceeded, dispatched: true);
            await GraphCycleFrontierPublicReads.MatrixAsync(admin, reader, state, token);
        }
        if (state.Hidden)
        {
            var request = new ReadIncomingGraphEdgesRequestV1(1, state.A, state.Seed.Graph, 7);
            var sdk = await global::KeyLoad.Client.GraphIncomingEdgesClient.IncomingEdgesAsync(reader.Sdk, request, token);
            await Assert.That(sdk.IsSuccess).IsFalse();
            await Assert.That(sdk.Problem!.ErrorCode).IsEqualTo(ErrorCode.NotFound.ToString());
            await Assert.That(sdk.Value).IsNull();
            await GraphCycleFrontierPublicReads.MatrixAsync(admin, reader, state, token);
            await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(McpCallerTools.GraphIncomingEdges, request, token),
                ErrorCode.NotFound, dispatched: true);
            await GraphCycleFrontierPublicReads.MatrixAsync(admin, reader, state, token);
        }
    }
}
