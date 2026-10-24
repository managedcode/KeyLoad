using System.Globalization;
using System.Text.Json;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class FunctionalGateGraphs
{
    private const int GraphCount = 3;
    private const int Depth = 1;
    private const int MaximumVertices = 2;
    private const int MaximumEdges = 1;
    private const long Initial = 0L;
    private const long Created = 1L;
    private const long Updated = 2L;
    private const long Deleted = 3L;
    private const string GraphPrefix = "gate_graph_";
    private const string VertexPrefix = "graph-vertex-";
    private const string Separator = "-";
    private const string Label = "gate-link";
    private const string InitialState = "created";
    private const string UpdatedState = "updated";
    private const string PutKind = "putDocument";
    private const string DeleteKind = "deleteDocument";
    private const string LinkKind = "upsertEdge";
    private const string UnlinkKind = "deleteEdge";

    internal static async Task InitializeAsync(FunctionalGateContext ctx, CancellationToken token)
    {
        await Assert.That(ctx.ResourceCount).IsEqualTo(GraphCount);
        for (var index = Initial; index < ctx.ResourceCount; index++)
        {
            var definition = new ResourceDefinition(GraphPrefix + index.ToString(CultureInfo.InvariantCulture),
                ResourceKind.Graph, ctx.Partition.TransactionDomainId);
            var request = new ConfigureResourceRequest(ctx.Partition.TenantId, ctx.Partition.DatabaseId, definition);
            var commandId = Guid.NewGuid();
            await SqlClusterProtocol.EqualAsync(definition, await ctx.SdkAsync(
                ctx.Administrator.Sdk.ConfigureResourceAsync(commandId, request, token)));
            await SqlClusterProtocol.EqualAsync(definition, await ctx.McpHeaderAsync<ResourceDefinition>(
                ctx.Administrator, McpCallerTools.ResourcesConfigure, request, commandId, token));
        }
    }

    internal static async Task ExerciseAsync(FunctionalGateContext ctx, int iteration, CancellationToken token)
    {
        await Assert.That(ctx.ResourceCount).IsEqualTo(GraphCount);
        for (var index = Initial; index < ctx.ResourceCount; index++)
        {
            var suffix = index.ToString(CultureInfo.InvariantCulture) + Separator + iteration.ToString(CultureInfo.InvariantCulture);
            var graph = GraphPrefix + index.ToString(CultureInfo.InvariantCulture);
            var vertex = ctx.Bind(new EntityRef(ctx.Partition, ctx.MutableDocumentCollection, VertexPrefix + suffix));
            var edgeId = VertexPrefix + suffix;
            var createdJson = DocumentJson(vertex, InitialState);
            var updatedJson = DocumentJson(vertex, UpdatedState);
            await CommitAsync(ctx, [new PutDocument(vertex.Collection, vertex.Id, createdJson, Initial)],
                [new(PutKind, vertex.Collection, vertex.Id, Created)], false, token);
            await DocumentAsync(ctx, vertex, new(vertex, Created, createdJson, false, []), token);
            await CommitAsync(ctx, [new UpsertEdge(graph, edgeId, vertex, ctx.Bind(ctx.Anchor), Label, createdJson, Initial)],
                [new(LinkKind, graph, edgeId, Created)], true, token);
            await TraverseAsync(ctx, graph, vertex, new(edgeId, vertex, ctx.Bind(ctx.Anchor), Label, createdJson, Created), token);
            await CommitAsync(ctx, [new PutDocument(vertex.Collection, vertex.Id, updatedJson, Created),
                new UpsertEdge(graph, edgeId, vertex, ctx.Bind(ctx.Anchor), Label, updatedJson, Created)],
                [new(PutKind, vertex.Collection, vertex.Id, Updated), new(LinkKind, graph, edgeId, Updated)], false, token);
            await DocumentAsync(ctx, vertex, new(vertex, Updated, updatedJson, false, []), token);
            await TraverseAsync(ctx, graph, vertex, new(edgeId, vertex, ctx.Bind(ctx.Anchor), Label, updatedJson, Updated), token);
            await CommitAsync(ctx, [new DeleteEdge(graph, edgeId, Updated)],
                [new(UnlinkKind, graph, edgeId, Deleted)], true, token);
            await TraverseAsync(ctx, graph, vertex, null, token);
            await CommitAsync(ctx, [new DeleteDocument(vertex.Collection, vertex.Id, Updated)],
                [new(DeleteKind, vertex.Collection, vertex.Id, Deleted)], false, token);
            await DocumentAsync(ctx, vertex, null, token);
        }
    }

    private static string DocumentJson(EntityRef vertex, string state)
        => JsonSerializer.Serialize(new
        {
            id = vertex.Id,
            state
        }, JsonDefaults.Options);

    private static async Task CommitAsync(FunctionalGateContext ctx, Mutation[] mutations,
        MutationReceipt[] expected, bool officialFirst, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), ctx.Partition, [.. mutations]);
        CommitReceipt receipt;
        CommitReceipt replay;
        if (officialFirst)
        {
            receipt = await ctx.McpAsync<CommitReceipt>(ctx.Writer, McpCallerTools.DocumentsCommit, command, token);
            replay = await ctx.SdkAsync(ctx.Writer.Sdk.CommitAsync(command, token));
        }
        else
        {
            receipt = await ctx.SdkAsync(ctx.Writer.Sdk.CommitAsync(command, token));
            replay = await ctx.McpAsync<CommitReceipt>(ctx.Writer, McpCallerTools.DocumentsCommit, command, token);
        }
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(ctx.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsNotEqualTo(Guid.Empty);
        await Assert.That(receipt.Token.Position).IsGreaterThan(Initial);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(ctx.ReplicaCount);
        await SqlClusterProtocol.EqualAsync(expected, receipt.Mutations.ToArray());
        await SqlClusterProtocol.EqualAsync(receipt, replay);
    }

    private static async Task DocumentAsync(FunctionalGateContext ctx, EntityRef vertex,
        DocumentResult? expected, CancellationToken token)
    {
        var sdk = await ctx.SdkAsync(ctx.Reader.Sdk.GetAsync(vertex, token));
        var mcp = await ctx.McpAsync<DocumentReadResult>(ctx.Reader, McpCallerTools.DocumentsGet,
            new GetDocumentRequest(vertex), token);
        await SqlClusterProtocol.EqualAsync(new DocumentReadResult(expected), sdk);
        await SqlClusterProtocol.EqualAsync(new DocumentReadResult(expected), mcp);
    }

    private static async Task TraverseAsync(FunctionalGateContext ctx, string graph,
        EntityRef vertex, EdgeRecord? edge, CancellationToken token)
    {
        var request = new TraverseRequest(ctx.Partition, graph, vertex, Depth, MaximumVertices, MaximumEdges);
        var expected = edge is null ? new global::KeyLoad.GraphTraversal([vertex], [])
            : new global::KeyLoad.GraphTraversal([.. new[] { vertex, ctx.Bind(ctx.Anchor) }
                .OrderBy(item => item.Collection, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.Ordinal)], [edge]);
        await SqlClusterProtocol.EqualAsync(expected, await ctx.SdkAsync(ctx.Reader.Sdk.TraverseAsync(request, token)));
        await SqlClusterProtocol.EqualAsync(expected, await ctx.McpAsync<global::KeyLoad.GraphTraversal>(
            ctx.Reader, McpCallerTools.GraphTraverse, request, token));
    }
}
