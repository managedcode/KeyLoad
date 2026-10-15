using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using P = KeyLoad.IntegrationTests.Features.Search.GraphSearchPathPolicyRf3Protocol;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class GraphSearchPathPolicyRf3Current
{
    internal static async Task RunAsync(RequestCqrsRf3Callers root, GraphSearchPathPolicyRf3State state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => DocumentsAsync(root, state, token), failures);
        await ServerFailureObserver.ObserveAsync(() => GraphAsync(root, state.Partition, token), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await SqlRf3Protocol.EqualAsync(state.Outbox, await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.OutboxStatusAsync(state.Partition, token)));
            await SqlRf3Protocol.EqualAsync(state.Outbox, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(await root.Mcp.CallAsync(
                McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Partition), token))).Value);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task DocumentsAsync(RequestCqrsRf3Callers root, GraphSearchPathPolicyRf3State state, CancellationToken token)
    {
        DocumentResult[] expected =
        [GraphSearchPathPolicyRf3Recipe.Document(state.Partition, P.Projects, P.Root, P.RootJson),
         GraphSearchPathPolicyRf3Recipe.Document(state.Partition, P.Projects, P.Middle, P.MiddleJson, state.MiddleRevision),
         GraphSearchPathPolicyRf3Recipe.Document(state.Partition, P.Projects, P.Private, P.PrivateJson),
         GraphSearchPathPolicyRf3Recipe.Document(state.Partition, P.Documents, P.Direct, P.DirectJson),
         GraphSearchPathPolicyRf3Recipe.Document(state.Partition, P.Documents, P.Target, P.TargetJson)];
        foreach (var document in expected)
        {
            await SqlRf3Protocol.EqualAsync(new DocumentReadResult(document), await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.GetAsync(document.Reference, token)));
            await SqlRf3Protocol.EqualAsync(document, (await PublicReadAssertions.RequiredDocumentAsync(await root.Mcp.CallAsync(
                McpCallerTools.DocumentsGet, new GetDocumentRequest(document.Reference), token))).Value);
        }
    }

    private static async Task GraphAsync(RequestCqrsRf3Callers root, PartitionRef partition, CancellationToken token)
    {
        var request = new TraverseRequest(partition, P.Graph,
            GraphSearchPathPolicyRf3Recipe.Vertex(partition, P.Projects, P.Root), P.Depth, P.Vertices, P.Edges);
        var expected = GraphSearchPathPolicyRf3Recipe.Graph(partition);
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.TraverseAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<global::KeyLoad.GraphTraversal>(await root.Mcp.CallAsync(
            McpCallerTools.GraphTraverse, request, token))).Value);
    }
}
