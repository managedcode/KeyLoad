using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;
using KeyLoad.Server;
using P = KeyLoad.ClusterTests.Search.GraphSearchPathPolicyClusterProtocol;

namespace KeyLoad.ClusterTests.Search;

internal static class GraphSearchPathPolicyClusterCurrent
{
    internal static async Task RunAsync(RequestCqrsClusterCallers root, GraphSearchPathPolicyClusterState state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => DocumentsAsync(root, state, token), failures);
        await ServerFailureObserver.ObserveAsync(() => GraphAsync(root, state, token), failures);
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
        [GraphSearchPathPolicyClusterRecipe.Document(state, P.Projects, P.Root, P.RootJson),
         GraphSearchPathPolicyClusterRecipe.Document(state, P.Projects, P.Middle, P.MiddleJson, state.MiddleRevision),
         GraphSearchPathPolicyClusterRecipe.Document(state, P.Projects, P.Private, P.PrivateJson),
         GraphSearchPathPolicyClusterRecipe.Document(state, P.Documents, P.Direct, P.DirectJson),
         GraphSearchPathPolicyClusterRecipe.Document(state, P.Documents, P.Target, P.TargetJson)];
        foreach (var document in expected)
        {
            var reference = GraphSearchPathPolicyClusterRecipe.Vertex(state.Partition, document.Reference.Collection, document.Reference.Id);
            await SqlClusterProtocol.EqualAsync(new DocumentReadResult(document), await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.GetAsync(reference, token)));
            await SqlClusterProtocol.EqualAsync(document, (await PublicReadAssertions.RequiredDocumentAsync(await root.Mcp.CallAsync(
                McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token))).Value);
        }
    }

    private static async Task GraphAsync(RequestCqrsClusterCallers root, GraphSearchPathPolicyClusterState state, CancellationToken token)
    {
        var request = new TraverseRequest(state.Partition, P.Graph,
            GraphSearchPathPolicyClusterRecipe.Vertex(state.Partition, P.Projects, P.Root), P.Depth, P.Vertices, P.Edges);
        var expected = GraphSearchPathPolicyClusterRecipe.Graph(state);
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.TraverseAsync(request, token)));
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<global::KeyLoad.GraphTraversal>(await root.Mcp.CallAsync(
            McpCallerTools.GraphTraverse, request, token))).Value);
    }
}
