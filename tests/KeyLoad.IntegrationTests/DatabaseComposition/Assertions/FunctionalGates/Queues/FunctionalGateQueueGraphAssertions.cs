using KeyLoad.IntegrationTests.ClientApi;
using P = KeyLoad.IntegrationTests.DatabaseComposition.FunctionalGateQueueGraphProtocol;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class FunctionalGateQueueGraphAssertions
{
    internal static async Task DocumentAsync(FunctionalGateContext ctx, EntityRef reference,
        bool present, CancellationToken token)
    {
        var expected = new DocumentReadResult(present
            ? new(reference, P.Created, P.DocumentJson, false, []) : null);
        await FunctionalGateQueues.EqualAsync(expected, await ctx.SdkAsync(ctx.Reader.Sdk.GetAsync(reference, token)));
        await FunctionalGateQueues.EqualAsync(expected, await ctx.McpAsync<DocumentReadResult>(ctx.Reader,
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token));
    }

    internal static async Task EdgeAsync(FunctionalGateContext ctx, QueueGraphLink link,
        string edgeId, bool present, CancellationToken token)
    {
        var request = new TraverseRequest(ctx.Partition, P.Graph, link.From,
            P.Depth, P.MaximumVertices, P.MaximumEdges);
        var vertices = new[] { link.From, link.To }.OrderBy(item => item.Collection, StringComparer.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var expected = present
            ? new global::KeyLoad.GraphTraversal([.. vertices],
                [new(edgeId, link.From, link.To, P.Label, P.Attributes, P.Created)])
            : new global::KeyLoad.GraphTraversal([link.From], []);
        await FunctionalGateQueues.EqualAsync(expected, await ctx.SdkAsync(ctx.Reader.Sdk.TraverseAsync(request, token)));
        await FunctionalGateQueues.EqualAsync(expected, await ctx.McpAsync<global::KeyLoad.GraphTraversal>(ctx.Reader,
            McpCallerTools.GraphTraverse, request, token));
    }
}
