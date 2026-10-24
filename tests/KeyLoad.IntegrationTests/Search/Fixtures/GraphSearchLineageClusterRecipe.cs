using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Search;

internal static class GraphSearchLineageClusterRecipe
{
    internal static EntityRef Vertex(PartitionRef partition, string id) => new(partition, GraphSearchLineageClusterProtocol.Collection, id);
    internal static EntityRef StoredVertex(PartitionRef partition, string id, EntityRefLifetime lifetime)
        => Vertex(partition, id) with { Lifetime = lifetime };
    internal static CommandRequest Seed(PartitionRef partition)
        => new(Guid.NewGuid(), partition,
        [new PutDocument(GraphSearchLineageClusterProtocol.Collection, GraphSearchLineageClusterProtocol.Alpha, GraphSearchLineageClusterProtocol.AlphaJson),
         new PutDocument(GraphSearchLineageClusterProtocol.Collection, GraphSearchLineageClusterProtocol.Beta, GraphSearchLineageClusterProtocol.BetaJson),
         new PutDocument(GraphSearchLineageClusterProtocol.Collection, GraphSearchLineageClusterProtocol.Context, GraphSearchLineageClusterProtocol.ContextJson),
         new UpsertEdge(GraphSearchLineageClusterProtocol.EarlyGraph, GraphSearchLineageClusterProtocol.EarlyEdge,
             Vertex(partition, GraphSearchLineageClusterProtocol.Alpha), Vertex(partition, GraphSearchLineageClusterProtocol.Beta), GraphSearchLineageClusterProtocol.Label),
         new UpsertEdge(GraphSearchLineageClusterProtocol.LateGraph, GraphSearchLineageClusterProtocol.LateFirstEdge,
             Vertex(partition, GraphSearchLineageClusterProtocol.Alpha), Vertex(partition, GraphSearchLineageClusterProtocol.Beta), GraphSearchLineageClusterProtocol.Label, GraphSearchLineageClusterProtocol.Attributes),
         new UpsertEdge(GraphSearchLineageClusterProtocol.LateGraph, GraphSearchLineageClusterProtocol.LateSecondEdge,
             Vertex(partition, GraphSearchLineageClusterProtocol.Beta), Vertex(partition, GraphSearchLineageClusterProtocol.Context), GraphSearchLineageClusterProtocol.Label, GraphSearchLineageClusterProtocol.Attributes),
         new PutVector(GraphSearchLineageClusterProtocol.Collection, GraphSearchLineageClusterProtocol.Alpha, GraphSearchLineageClusterProtocol.VectorField,
             [1f, 0f], GraphSearchLineageClusterProtocol.Space, 1L),
         new PutVector(GraphSearchLineageClusterProtocol.Collection, GraphSearchLineageClusterProtocol.Beta, GraphSearchLineageClusterProtocol.VectorField,
             [0f, 1f], GraphSearchLineageClusterProtocol.Space, 1L)]);

    internal static ImmutableArray<MutationReceipt> Receipts =>
        [new("putDocument", GraphSearchLineageClusterProtocol.Collection, GraphSearchLineageClusterProtocol.Alpha, 1L),
         new("putDocument", GraphSearchLineageClusterProtocol.Collection, GraphSearchLineageClusterProtocol.Beta, 1L),
         new("putDocument", GraphSearchLineageClusterProtocol.Collection, GraphSearchLineageClusterProtocol.Context, 1L),
         new("upsertEdge", GraphSearchLineageClusterProtocol.EarlyGraph, GraphSearchLineageClusterProtocol.EarlyEdge, 1L),
         new("upsertEdge", GraphSearchLineageClusterProtocol.LateGraph, GraphSearchLineageClusterProtocol.LateFirstEdge, 1L),
         new("upsertEdge", GraphSearchLineageClusterProtocol.LateGraph, GraphSearchLineageClusterProtocol.LateSecondEdge, 1L),
         new("putVector", GraphSearchLineageClusterProtocol.Collection, GraphSearchLineageClusterProtocol.Alpha, 1L),
         new("putVector", GraphSearchLineageClusterProtocol.Collection, GraphSearchLineageClusterProtocol.Beta, 1L)];

    internal static GraphSearchRequest Request(PartitionRef partition, bool expansion, ImmutableArray<string>? labels)
    {
        var early = new GraphWalkSpec(GraphSearchLineageClusterProtocol.EarlyGraph, [Vertex(partition, GraphSearchLineageClusterProtocol.Alpha)],
            GraphSearchLineageClusterProtocol.Depth, GraphSearchLineageClusterProtocol.Vertices, GraphSearchLineageClusterProtocol.Edges);
        var late = early with
        {
            Graph = GraphSearchLineageClusterProtocol.LateGraph,
            Labels = labels
        };
        var search = new SearchRequest(partition, GraphSearchLineageClusterProtocol.Collection, VectorField: GraphSearchLineageClusterProtocol.VectorField,
            Vector: [1f, 0f], Space: GraphSearchLineageClusterProtocol.Space, Limit: GraphSearchLineageClusterProtocol.ResultLimit,
            FusionConstant: GraphSearchLineageClusterProtocol.Fusion);
        return new(GraphSearchLineageClusterProtocol.Version, search, new(early), expansion ? null : new(late),
            expansion ? new(GraphSearchLineageClusterProtocol.LateGraph, GraphSearchLineageClusterProtocol.Depth,
                GraphSearchLineageClusterProtocol.Vertices, GraphSearchLineageClusterProtocol.Edges, labels) : null);
    }

    internal static GraphSearchResult Expected(PartitionRef partition, bool expansion, bool emptyLabels,
        EntityRefLifetime lifetime)
    {
        var firstBranches = expansion ? 1d : 2d;
        var secondBranches = expansion || emptyLabels ? 1d : 2d;
        return new(
            [new(new(StoredVertex(partition, GraphSearchLineageClusterProtocol.Alpha, lifetime), 1L, GraphSearchLineageClusterProtocol.AlphaJson, false, []), firstBranches / 61d),
             new(new(StoredVertex(partition, GraphSearchLineageClusterProtocol.Beta, lifetime), 1L, GraphSearchLineageClusterProtocol.BetaJson, false, []), secondBranches / 62d)],
            expansion ? new(emptyLabels ? [] : [new(new(StoredVertex(partition, GraphSearchLineageClusterProtocol.Context, lifetime), 1L,
                GraphSearchLineageClusterProtocol.ContextJson, false, []), 1)], GraphExpansionCompleteness.SelectedHits) : null);
    }
}
