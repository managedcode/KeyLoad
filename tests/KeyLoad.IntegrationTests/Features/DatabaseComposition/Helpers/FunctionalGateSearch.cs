using System.Globalization;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateSearch
{
    internal const int First = 0;
    internal const int CollectionCount = 3;
    internal const long Initial = 0L;
    internal const long Created = 1L;
    internal const long Updated = 2L;
    internal const long Deleted = 3L;
    internal const string VectorPrefix = "gate_vectors_";
    internal const string TextPrefix = "gate_text_";
    internal const string VectorField = "/embedding";
    internal const string TextField = "/text";
    internal const string OriginalJson = "{\"state\":\"original\"}";
    internal const string UpdatedJson = "{\"state\":\"updated\"}";
    internal const string NeedleJson = "{\"text\":\"needle\"}";
    internal const string OtherJson = "{\"text\":\"other\"}";
    internal const string Put = "putDocument";
    internal const string VectorPut = "putVector";
    internal const string Delete = "deleteDocument";
    internal const double FirstRank = 1d / 61;
    internal const double SecondRank = 1d / 62;
    internal static readonly VectorSpace Space = new("gate-search-space", 2, DistanceMetric.DotProduct, "gate-model", "1");
    private const string BindingId = "functional-reference-binding";
    private const string BindingJson = "{\"kind\":\"reference-binding\"}";
    private const string Graph = "gate_graph_0";
    private const string Link = "upsertEdge";
    private const string Unlink = "deleteEdge";
    private const string Attributes = "{}";
    private const int Depth = 1;
    private const int Vertices = 2;
    private const int Edges = 1;

    internal static async Task InitializeAsync(FunctionalGateContext ctx, CancellationToken token)
    {
        await Assert.That(ctx.ResourceCount).IsEqualTo(CollectionCount);
        for (var index = First; index < CollectionCount; index++)
        {
            var suffix = index.ToString(CultureInfo.InvariantCulture);
            await ConfigureAsync(ctx, new(VectorPrefix + suffix, ResourceKind.Collection, ctx.Partition.TransactionDomainId)
            {
                VectorProfiles = [new(VectorField, Space)]
            }, token);
            await ConfigureAsync(ctx, new(TextPrefix + suffix, ResourceKind.Collection, ctx.Partition.TransactionDomainId), token);
            await CaptureAsync(ctx, VectorPrefix + suffix, token);
            await CaptureAsync(ctx, TextPrefix + suffix, token);
        }
    }

    private static async Task CaptureAsync(FunctionalGateContext ctx, string collection, CancellationToken token)
    {
        var reference = new EntityRef(ctx.Partition, collection, BindingId);
        await CommitAsync(ctx, [new PutDocument(collection, BindingId, BindingJson, Initial)],
            [new(Put, collection, BindingId, Created)], officialFirst: false, token);
        await FunctionalGateEntityReferences.CaptureAsync(ctx, ctx.Administrator,
            new(reference, Created, BindingJson, false, []), token);
    }

    internal static async Task ExerciseAsync(FunctionalGateContext ctx, int iteration, CancellationToken token)
    {
        var stem = "search-" + iteration.ToString(CultureInfo.InvariantCulture);
        for (var index = First; index < CollectionCount; index++)
        {
            var suffix = index.ToString(CultureInfo.InvariantCulture);
            await FunctionalGateSearchVectors.RunAsync(ctx, VectorPrefix + suffix, stem, token);
            await FunctionalGateSearchText.RunAsync(ctx, TextPrefix + suffix, stem, token);
        }
    }

    private static async Task ConfigureAsync(FunctionalGateContext ctx, ResourceDefinition definition, CancellationToken token)
    {
        var commandId = Guid.NewGuid();
        var request = new ConfigureResourceRequest(ctx.Partition.TenantId, ctx.Partition.DatabaseId, definition);
        await SqlRf3Protocol.EqualAsync(definition, await ctx.SdkAsync(
            ctx.Administrator.Sdk.ConfigureResourceAsync(commandId, request, token)));
        await SqlRf3Protocol.EqualAsync(definition, await ctx.McpHeaderAsync<ResourceDefinition>(
            ctx.Administrator, McpCallerTools.ResourcesConfigure, request, commandId, token));
        FunctionalGateCreationLifetime.Expect(ctx, commandId, definition, table: false);
    }

    internal static async Task<CommitReceipt> CommitAsync(FunctionalGateContext ctx, Mutation[] mutations,
        MutationReceipt[] expected, bool officialFirst, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), ctx.Partition, [.. mutations]);
        var receipt = officialFirst
            ? await ctx.McpAsync<CommitReceipt>(ctx.Writer, McpCallerTools.DocumentsCommit, command, token)
            : await ctx.SdkAsync(ctx.Writer.Sdk.CommitAsync(command, token));
        var replay = officialFirst
            ? await ctx.SdkAsync(ctx.Writer.Sdk.CommitAsync(command, token))
            : await ctx.McpAsync<CommitReceipt>(ctx.Writer, McpCallerTools.DocumentsCommit, command, token);
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(ctx.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsNotEqualTo(Guid.Empty);
        await Assert.That(receipt.Token.Position).IsGreaterThan(Initial);
        await Assert.That(receipt.Token.OwnershipEpoch).IsGreaterThan(Initial);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(expected, receipt.Mutations.ToArray());
        await SqlRf3Protocol.EqualAsync(receipt, replay);
        return receipt;
    }

    internal static RankedDocument Hit(FunctionalGateContext ctx, string collection, string id,
        long revision, string json, double score) => new(new(ctx.Bind(new(ctx.Partition, collection, id)), revision, json, false, []), score);

    internal static async Task SearchAsync(FunctionalGateContext ctx, SearchRequest request,
        RankedDocument[] expected, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(expected, await ctx.SdkAsync(ctx.Reader.Sdk.SearchAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected, await ctx.McpAsync<RankedDocument[]>(
            ctx.Reader, McpCallerTools.SearchExecute, request, token));
    }

    internal static async Task LinkAndUnlinkAsync(FunctionalGateContext ctx, string collection,
        string id, CancellationToken token)
    {
        var vertex = ctx.Bind(new(ctx.Partition, collection, id));
        var edgeId = collection + "-" + id;
        await CommitAsync(ctx, [new UpsertEdge(Graph, edgeId, vertex, ctx.Bind(ctx.Anchor), edgeId, Attributes, Initial)],
            [new(Link, Graph, edgeId, Created)], true, token);
        await TraverseAsync(ctx, vertex, edgeId, new(edgeId, vertex, ctx.Bind(ctx.Anchor), edgeId, Attributes, Created), token);
        await CommitAsync(ctx, [new DeleteEdge(Graph, edgeId, Created)], [new(Unlink, Graph, edgeId, Updated)], false, token);
        await TraverseAsync(ctx, vertex, edgeId, null, token);
    }

    private static async Task TraverseAsync(FunctionalGateContext ctx, EntityRef vertex, string label,
        EdgeRecord? edge, CancellationToken token)
    {
        var request = new TraverseRequest(ctx.Partition, Graph, vertex, Depth, Vertices, Edges, [label]);
        var expected = edge is null ? new global::KeyLoad.GraphTraversal([vertex], [])
            : new global::KeyLoad.GraphTraversal([.. new[] { vertex, ctx.Bind(ctx.Anchor) }
                .OrderBy(item => item.Collection, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.Ordinal)], [edge]);
        await SqlRf3Protocol.EqualAsync(expected, await ctx.SdkAsync(ctx.Reader.Sdk.TraverseAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected, await ctx.McpAsync<global::KeyLoad.GraphTraversal>(
            ctx.Reader, McpCallerTools.GraphTraverse, request, token));
    }
}
