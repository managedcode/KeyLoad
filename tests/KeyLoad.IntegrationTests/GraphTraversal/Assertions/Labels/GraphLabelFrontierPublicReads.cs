using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.GraphTraversal;

internal static class GraphLabelFrontierPublicReads
{
    internal static async Task MatrixAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
        GraphLabelFrontierPublicState state, CancellationToken token)
    {
        foreach (var profile in new[] { 0, 2, 4, 5 })
        {
            for (var depth = 1; depth <= 3; depth++)
            {
                await ResultAsync(admin, reader, state, depth, profile, token);
            }
        }
        foreach (var profile in new[] { 1, 3, 6 })
        {
            await ResultAsync(admin, reader, state, 3, profile, token);
        }
    }

    private static async Task ResultAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
        GraphLabelFrontierPublicState state, int depth, int profile, CancellationToken token)
    {
        var expected = state.Expected(depth, profile);
        var request = state.Request(depth, profile, expected.Vertices.Length, state.Candidates(depth, profile));
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await reader.Sdk.TraverseAsync(request, token)));
        await GraphLabelFrontierPublicCurrent.ObserveAsync(admin, state, token);
        var reply = await reader.Mcp.CallAsync(McpCallerTools.GraphTraverse, request, token);
        await SqlClusterProtocol.EqualAsync(expected,
            (await McpCallerAssertions.SuccessAsync<global::KeyLoad.GraphTraversal>(reply)).Value);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Seed.Reader.Secret, GraphIncomingClusterScenario.PrivateMarker);
        await GraphLabelFrontierPublicCurrent.ObserveAsync(admin, state, token);
    }
}
