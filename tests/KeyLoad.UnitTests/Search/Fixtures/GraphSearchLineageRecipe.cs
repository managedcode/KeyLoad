using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Search;

internal static class GraphSearchLineageRecipe
{
    internal static EntityRef Vertex(PartitionRef partition, string id) => new(partition, GraphSearchLineageProtocol.Collection, id);
    internal static CommandRequest Seed(PartitionRef partition)
        => new(Guid.NewGuid(), partition,
        [new PutDocument(GraphSearchLineageProtocol.Collection, GraphSearchLineageProtocol.Alpha, GraphSearchLineageProtocol.AlphaJson),
         new PutDocument(GraphSearchLineageProtocol.Collection, GraphSearchLineageProtocol.Beta, GraphSearchLineageProtocol.BetaJson),
         new PutDocument(GraphSearchLineageProtocol.Collection, GraphSearchLineageProtocol.Context, GraphSearchLineageProtocol.ContextJson),
         new UpsertEdge(GraphSearchLineageProtocol.EarlyGraph, GraphSearchLineageProtocol.EarlyEdge,
             Vertex(partition, GraphSearchLineageProtocol.Alpha), Vertex(partition, GraphSearchLineageProtocol.Beta), GraphSearchLineageProtocol.Label),
         new UpsertEdge(GraphSearchLineageProtocol.LateGraph, GraphSearchLineageProtocol.LateFirstEdge,
             Vertex(partition, GraphSearchLineageProtocol.Alpha), Vertex(partition, GraphSearchLineageProtocol.Beta), GraphSearchLineageProtocol.Label, GraphSearchLineageProtocol.Attributes),
         new UpsertEdge(GraphSearchLineageProtocol.LateGraph, GraphSearchLineageProtocol.LateSecondEdge,
             Vertex(partition, GraphSearchLineageProtocol.Beta), Vertex(partition, GraphSearchLineageProtocol.Context), GraphSearchLineageProtocol.Label, GraphSearchLineageProtocol.Attributes),
         new PutVector(GraphSearchLineageProtocol.Collection, GraphSearchLineageProtocol.Alpha, GraphSearchLineageProtocol.VectorField,
             [1f, 0f], GraphSearchLineageProtocol.Space, 1L),
         new PutVector(GraphSearchLineageProtocol.Collection, GraphSearchLineageProtocol.Beta, GraphSearchLineageProtocol.VectorField,
             [0f, 1f], GraphSearchLineageProtocol.Space, 1L)]);

    internal static ImmutableArray<MutationReceipt> Receipts =>
        [new("putDocument", GraphSearchLineageProtocol.Collection, GraphSearchLineageProtocol.Alpha, 1L),
         new("putDocument", GraphSearchLineageProtocol.Collection, GraphSearchLineageProtocol.Beta, 1L),
         new("putDocument", GraphSearchLineageProtocol.Collection, GraphSearchLineageProtocol.Context, 1L),
         new("upsertEdge", GraphSearchLineageProtocol.EarlyGraph, GraphSearchLineageProtocol.EarlyEdge, 1L),
         new("upsertEdge", GraphSearchLineageProtocol.LateGraph, GraphSearchLineageProtocol.LateFirstEdge, 1L),
         new("upsertEdge", GraphSearchLineageProtocol.LateGraph, GraphSearchLineageProtocol.LateSecondEdge, 1L),
         new("putVector", GraphSearchLineageProtocol.Collection, GraphSearchLineageProtocol.Alpha, 1L),
         new("putVector", GraphSearchLineageProtocol.Collection, GraphSearchLineageProtocol.Beta, 1L)];

    internal static GraphSearchRequest Request(PartitionRef partition, bool expansion, ImmutableArray<string>? labels)
    {
        var early = new GraphWalkSpec(GraphSearchLineageProtocol.EarlyGraph, [Vertex(partition, GraphSearchLineageProtocol.Alpha)],
            GraphSearchLineageProtocol.Depth, GraphSearchLineageProtocol.Vertices, GraphSearchLineageProtocol.Edges);
        var late = early with
        {
            Graph = GraphSearchLineageProtocol.LateGraph,
            Labels = labels
        };
        var search = new SearchRequest(partition, GraphSearchLineageProtocol.Collection, VectorField: GraphSearchLineageProtocol.VectorField,
            Vector: [1f, 0f], Space: GraphSearchLineageProtocol.Space, Limit: GraphSearchLineageProtocol.ResultLimit,
            FusionConstant: GraphSearchLineageProtocol.Fusion);
        return new(GraphSearchLineageProtocol.Version, search, new(early), expansion ? null : new(late),
            expansion ? new(GraphSearchLineageProtocol.LateGraph, GraphSearchLineageProtocol.Depth,
                GraphSearchLineageProtocol.Vertices, GraphSearchLineageProtocol.Edges, labels) : null);
    }

    internal static GraphSearchResult Expected(TestDatabase database, bool expansion, bool emptyLabels)
    {
        var firstBranches = expansion ? 1d : 2d;
        var secondBranches = expansion || emptyLabels ? 1d : 2d;
        return new(
            [new(new(database.ExpectedReference(Vertex(database.Partition, GraphSearchLineageProtocol.Alpha)), 1L, GraphSearchLineageProtocol.AlphaJson, false, []), firstBranches / 61d),
             new(new(database.ExpectedReference(Vertex(database.Partition, GraphSearchLineageProtocol.Beta)), 1L, GraphSearchLineageProtocol.BetaJson, false, []), secondBranches / 62d)],
            expansion ? new(emptyLabels ? [] : [new(new(database.ExpectedReference(Vertex(database.Partition, GraphSearchLineageProtocol.Context)), 1L,
                GraphSearchLineageProtocol.ContextJson, false, []), 1)], GraphExpansionCompleteness.SelectedHits) : null);
    }
}
