using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphPathFieldUseR43Current
{
    private const string CanonicalFullAttributes = "{\"public\":\"visible\",\"secret\":\"graph-path-private-marker\"}";

    internal static async Task ObserveAsync(RequestCqrsRf3Callers admin, GraphPathFieldUseR43State state,
        CancellationToken token)
    {
        foreach (var vertex in state.Vertices)
        {
            var expected = new DocumentResult(vertex, 1L, "{}", false, []);
            await SqlRf3Protocol.EqualAsync(expected, await PublicReadAssertions.DocumentAsync(await admin.Sdk.GetAsync(vertex, token)));
            await SqlRf3Protocol.EqualAsync(expected, (await PublicReadAssertions.DocumentAsync(
                await admin.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(vertex), token))).Value);
        }
        var request = new TraverseRequest(state.Seed.Partition, GraphPathRf3Scenario.Graph, state.Source, 2, 3, 2);
        var expectedGraph = new global::KeyLoad.GraphTraversal([state.Middle, state.Source, state.Target],
            [.. state.Edges.Select(edge => edge with { AttributesJson = CanonicalFullAttributes })]);
        await SqlRf3Protocol.EqualAsync(expectedGraph, await McpCallerAssertions.SdkSuccessAsync(
            await admin.Sdk.TraverseAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expectedGraph, (await McpCallerAssertions.SuccessAsync<global::KeyLoad.GraphTraversal>(
            await admin.Mcp.CallAsync(McpCallerTools.GraphTraverse, request, token))).Value);
        await SqlRf3Protocol.EqualAsync(state.Outbox, await OutboxAsync(admin, state, token));
    }

    internal static async Task<OutboxStatus> OutboxAsync(RequestCqrsRf3Callers admin,
        GraphPathFieldUseR43State state, CancellationToken token)
    {
        var current = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.OutboxStatusAsync(state.Seed.Partition, token));
        await SqlRf3Protocol.EqualAsync(current, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await admin.Mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Seed.Partition), token))).Value);
        return current;
    }
}
