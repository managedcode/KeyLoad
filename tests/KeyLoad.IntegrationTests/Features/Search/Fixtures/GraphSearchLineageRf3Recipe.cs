using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class GraphSearchLineageRf3Recipe
{
    internal static EntityRef Vertex(PartitionRef partition, string id) => new(partition, GraphSearchLineageRf3Protocol.Collection, id);
    internal static CommandRequest Seed(PartitionRef partition)
        => new(Guid.NewGuid(), partition,
        [new PutDocument(GraphSearchLineageRf3Protocol.Collection, GraphSearchLineageRf3Protocol.Alpha, GraphSearchLineageRf3Protocol.AlphaJson),
         new PutDocument(GraphSearchLineageRf3Protocol.Collection, GraphSearchLineageRf3Protocol.Beta, GraphSearchLineageRf3Protocol.BetaJson),
         new PutDocument(GraphSearchLineageRf3Protocol.Collection, GraphSearchLineageRf3Protocol.Context, GraphSearchLineageRf3Protocol.ContextJson),
         new UpsertEdge(GraphSearchLineageRf3Protocol.EarlyGraph, GraphSearchLineageRf3Protocol.EarlyEdge,
             Vertex(partition, GraphSearchLineageRf3Protocol.Alpha), Vertex(partition, GraphSearchLineageRf3Protocol.Beta), GraphSearchLineageRf3Protocol.Label),
         new UpsertEdge(GraphSearchLineageRf3Protocol.LateGraph, GraphSearchLineageRf3Protocol.LateFirstEdge,
             Vertex(partition, GraphSearchLineageRf3Protocol.Alpha), Vertex(partition, GraphSearchLineageRf3Protocol.Beta), GraphSearchLineageRf3Protocol.Label, GraphSearchLineageRf3Protocol.Attributes),
         new UpsertEdge(GraphSearchLineageRf3Protocol.LateGraph, GraphSearchLineageRf3Protocol.LateSecondEdge,
             Vertex(partition, GraphSearchLineageRf3Protocol.Beta), Vertex(partition, GraphSearchLineageRf3Protocol.Context), GraphSearchLineageRf3Protocol.Label, GraphSearchLineageRf3Protocol.Attributes),
         new PutVector(GraphSearchLineageRf3Protocol.Collection, GraphSearchLineageRf3Protocol.Alpha, GraphSearchLineageRf3Protocol.VectorField,
             [1f, 0f], GraphSearchLineageRf3Protocol.Space, 1L),
         new PutVector(GraphSearchLineageRf3Protocol.Collection, GraphSearchLineageRf3Protocol.Beta, GraphSearchLineageRf3Protocol.VectorField,
             [0f, 1f], GraphSearchLineageRf3Protocol.Space, 1L)]);

    internal static ImmutableArray<MutationReceipt> Receipts =>
        [new("putDocument", GraphSearchLineageRf3Protocol.Collection, GraphSearchLineageRf3Protocol.Alpha, 1L),
         new("putDocument", GraphSearchLineageRf3Protocol.Collection, GraphSearchLineageRf3Protocol.Beta, 1L),
         new("putDocument", GraphSearchLineageRf3Protocol.Collection, GraphSearchLineageRf3Protocol.Context, 1L),
         new("upsertEdge", GraphSearchLineageRf3Protocol.EarlyGraph, GraphSearchLineageRf3Protocol.EarlyEdge, 1L),
         new("upsertEdge", GraphSearchLineageRf3Protocol.LateGraph, GraphSearchLineageRf3Protocol.LateFirstEdge, 1L),
         new("upsertEdge", GraphSearchLineageRf3Protocol.LateGraph, GraphSearchLineageRf3Protocol.LateSecondEdge, 1L),
         new("putVector", GraphSearchLineageRf3Protocol.Collection, GraphSearchLineageRf3Protocol.Alpha, 1L),
         new("putVector", GraphSearchLineageRf3Protocol.Collection, GraphSearchLineageRf3Protocol.Beta, 1L)];

    internal static GraphSearchRequest Request(PartitionRef partition, bool expansion, ImmutableArray<string>? labels)
    {
        var early = new GraphWalkSpec(GraphSearchLineageRf3Protocol.EarlyGraph, [Vertex(partition, GraphSearchLineageRf3Protocol.Alpha)],
            GraphSearchLineageRf3Protocol.Depth, GraphSearchLineageRf3Protocol.Vertices, GraphSearchLineageRf3Protocol.Edges);
        var late = early with
        {
            Graph = GraphSearchLineageRf3Protocol.LateGraph,
            Labels = labels
        };
        var search = new SearchRequest(partition, GraphSearchLineageRf3Protocol.Collection, VectorField: GraphSearchLineageRf3Protocol.VectorField,
            Vector: [1f, 0f], Space: GraphSearchLineageRf3Protocol.Space, Limit: GraphSearchLineageRf3Protocol.ResultLimit,
            FusionConstant: GraphSearchLineageRf3Protocol.Fusion);
        return new(GraphSearchLineageRf3Protocol.Version, search, new(early), expansion ? null : new(late),
            expansion ? new(GraphSearchLineageRf3Protocol.LateGraph, GraphSearchLineageRf3Protocol.Depth,
                GraphSearchLineageRf3Protocol.Vertices, GraphSearchLineageRf3Protocol.Edges, labels) : null);
    }

    internal static GraphSearchResult Expected(PartitionRef partition, bool expansion, bool emptyLabels)
    {
        var firstBranches = expansion ? 1d : 2d;
        var secondBranches = expansion || emptyLabels ? 1d : 2d;
        return new(
            [new(new(Vertex(partition, GraphSearchLineageRf3Protocol.Alpha), 1L, GraphSearchLineageRf3Protocol.AlphaJson, false, []), firstBranches / 61d),
             new(new(Vertex(partition, GraphSearchLineageRf3Protocol.Beta), 1L, GraphSearchLineageRf3Protocol.BetaJson, false, []), secondBranches / 62d)],
            expansion ? new(emptyLabels ? [] : [new(new(Vertex(partition, GraphSearchLineageRf3Protocol.Context), 1L,
                GraphSearchLineageRf3Protocol.ContextJson, false, []), 1)], GraphExpansionCompleteness.SelectedHits) : null);
    }
}
