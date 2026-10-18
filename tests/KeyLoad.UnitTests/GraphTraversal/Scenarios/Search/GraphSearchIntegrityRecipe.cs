using System.Collections.Immutable;
using P = KeyLoad.UnitTests.Features.GraphTraversal.GraphTraversalIntegrityFixture;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphSearchIntegrityRecipe
{
    private const int Version = 1;
    private const int SeedDepth = 0;
    private const int Fusion = 60;
    private const int FirstRank = 1;
    private const long PlacementEpoch = 1L;
    private const string PutKind = "putDocument";
    private const string EdgeKind = "upsertEdge";

    internal static GraphSearchRequest Request(GraphTraversalIntegrityState state,
        GraphSearchIntegrityOperator selected, bool filtered)
    {
        ImmutableArray<string>? labels = filtered ? [P.OtherLabel] : null;
        var walk = new GraphWalkSpec(P.Graph, [state.HealthyEdge.From], P.Depth, P.MaximumVertices, P.MaximumEdges, labels);
        var seed = walk with
        {
            MaxDepth = SeedDepth,
            Labels = null
        };
        return new(Version, new(state.SeedCommand.Partition, P.Nodes, Limit: P.MaximumVertices, FusionConstant: Fusion),
            Scope: selected == GraphSearchIntegrityOperator.Scope ? new(walk) : null,
            Retriever: new(selected == GraphSearchIntegrityOperator.Retriever ? walk : seed),
            Expansion: selected == GraphSearchIntegrityOperator.Expansion
                ? new(P.Graph, P.Depth, P.MaximumVertices, P.MaximumEdges, labels) : null);
    }

    internal static GraphSearchResult Expected(GraphTraversalIntegrityState state,
        GraphTraversalIntegrityContinuation? continuation, GraphSearchIntegrityOperator selected, bool filtered)
    {
        EntityRef[] targets = filtered ? [] : continuation is null
            ? [state.DamagedEdge.To, state.HealthyEdge.To]
            : [continuation.Edge.To, state.DamagedEdge.To, state.HealthyEdge.To];
        var hits = new List<RankedDocument>
        {
            new(Document(state.HealthyEdge.From), 1d / (Fusion + FirstRank))
        };
        if (selected == GraphSearchIntegrityOperator.Retriever)
        {
            var rank = FirstRank;
            foreach (var target in targets)
            {
                hits.Add(new(Document(target), 1d / (Fusion + ++rank)));
            }
        }
        return new([.. hits], selected == GraphSearchIntegrityOperator.Expansion
            ? new([.. targets.Select(target => new GraphContextDocument(Document(target), P.Depth))],
                GraphExpansionCompleteness.SelectedHits) : null);
    }

    internal static CommitReceipt SeedReceipt(GraphTraversalIntegrityState state, Guid incarnation, long position)
        => new(state.SeedCommand.CommandId, new(incarnation, state.SeedCommand.Partition.AtomicPartitionId, position), [new(PutKind, P.Nodes, state.HealthyEdge.From.Id, P.InitialRevision),
             new(PutKind, P.Nodes, state.HealthyEdge.To.Id, P.InitialRevision),
             new(PutKind, P.Nodes, state.DamagedEdge.To.Id, P.InitialRevision),
             new(EdgeKind, P.Graph, state.HealthyEdge.Id, P.InitialRevision),
             new(EdgeKind, P.Graph, state.DamagedEdge.Id, P.InitialRevision)], DurabilityProfile.ProcessDurable, 1);

    internal static CommitReceipt ContinuedReceipt(GraphTraversalIntegrityContinuation continuation,
        Guid incarnation, long position)
        => new(continuation.Command.CommandId, new(incarnation, continuation.Command.Partition.AtomicPartitionId, position), [new(PutKind, P.Nodes, continuation.Edge.To.Id, P.InitialRevision),
             new(EdgeKind, P.Graph, continuation.Edge.Id, P.InitialRevision)], DurabilityProfile.ProcessDurable, 1);

    private static DocumentResult Document(EntityRef reference) => new(reference, P.InitialRevision, P.Attributes, false, []);
}
