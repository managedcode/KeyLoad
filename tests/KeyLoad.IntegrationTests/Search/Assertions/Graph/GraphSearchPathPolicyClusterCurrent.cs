using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using P = KeyLoad.IntegrationTests.Features.Search.GraphSearchPathPolicyClusterProtocol;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class GraphSearchPathPolicyClusterCurrent
{
    internal static async Task RunAsync(RequestCqrsClusterCallers root, GraphSearchPathPolicyClusterState state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => DocumentsAsync(root, state, token), failures);
        await ServerFailureObserver.ObserveAsync(() => GraphAsync(root, state.Partition, token), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await SqlClusterProtocol.EqualAsync(state.Outbox, await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.OutboxStatusAsync(state.Partition, token)));
            await SqlClusterProtocol.EqualAsync(state.Outbox, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(await root.Mcp.CallAsync(
                McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Partition), token))).Value);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task DocumentsAsync(RequestCqrsClusterCallers root, GraphSearchPathPolicyClusterState state, CancellationToken token)
    {
        DocumentResult[] expected =
        [GraphSearchPathPolicyClusterRecipe.Document(state.Partition, P.Projects, P.Root, P.RootJson),
         GraphSearchPathPolicyClusterRecipe.Document(state.Partition, P.Projects, P.Middle, P.MiddleJson, state.MiddleRevision),
         GraphSearchPathPolicyClusterRecipe.Document(state.Partition, P.Projects, P.Private, P.PrivateJson),
         GraphSearchPathPolicyClusterRecipe.Document(state.Partition, P.Documents, P.Direct, P.DirectJson),
         GraphSearchPathPolicyClusterRecipe.Document(state.Partition, P.Documents, P.Target, P.TargetJson)];
        foreach (var document in expected)
        {
            await SqlClusterProtocol.EqualAsync(new DocumentReadResult(document), await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.GetAsync(document.Reference, token)));
            await SqlClusterProtocol.EqualAsync(document, (await PublicReadAssertions.RequiredDocumentAsync(await root.Mcp.CallAsync(
                McpCallerTools.DocumentsGet, new GetDocumentRequest(document.Reference), token))).Value);
        }
    }

    private static async Task GraphAsync(RequestCqrsClusterCallers root, PartitionRef partition, CancellationToken token)
    {
        var request = new TraverseRequest(partition, P.Graph,
            GraphSearchPathPolicyClusterRecipe.Vertex(partition, P.Projects, P.Root), P.Depth, P.Vertices, P.Edges);
        var expected = GraphSearchPathPolicyClusterRecipe.Graph(partition);
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.TraverseAsync(request, token)));
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<global::KeyLoad.GraphTraversal>(await root.Mcp.CallAsync(
            McpCallerTools.GraphTraverse, request, token))).Value);
    }
}
