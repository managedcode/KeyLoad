namespace KeyLoad.IntegrationTests.GraphTraversal;

internal sealed class GraphCycleFrontierPublicState(GraphIncomingClusterSeed seed)
{
    internal const string Owner = "cycle-reader";
    internal const string OtherOwner = "cycle-other";
    internal GraphIncomingClusterSeed Seed { get; } = seed;
    internal EntityRef Start => Vertex("r42-s");
    internal EntityRef A => Vertex("r42-a");
    internal EntityRef B => Vertex("r42-b");
    internal EntityRef Target => Vertex("r42-t");
    internal EntityRef[] Vertices => [A, B, Start, Target];
    internal bool Hidden
    {
        get; set;
    }
    internal long BranchRevision { get; set; } = 1L;
    internal OutboxStatus Outbox { get; set; } = null!;
    internal List<(CommandRequest Command, CommitReceipt Receipt)> Originals { get; } = [];
    internal EdgeRecord[] Edges =>
    [
        Edge("r42-0-loop", Start, Start), Edge("r42-1-sa", Start, A),
        Edge("r42-2-sb", Start, B), Edge("r42-3-at", A, Target),
        Edge("r42-4-bt", B, Target), Edge("r42-5-ts", Target, Start),
        Edge("r42-6-as", A, Start)
    ];

    internal global::KeyLoad.GraphTraversal Expected(int depth, bool hidden)
    {
        EntityRef[] vertices = depth == 1 ? hidden ? [B, Start] : [A, B, Start]
            : hidden ? [B, Start, Target] : [A, B, Start, Target];
        int[] indices = depth == 1 ? hidden ? [0, 2] : [0, 1, 2]
            : hidden ? depth == 2 ? [0, 2, 4] : [0, 2, 4, 5]
            : depth == 2 ? [0, 1, 2, 3, 4, 6] : [0, 1, 2, 3, 4, 5, 6];
        return Seed.StoredTraversal(new([.. vertices], [.. indices.Select(index => Edges[index])]));
    }

    internal TraverseRequest Request(int depth, int vertices, int edges)
        => new(Seed.TargetPartition, Seed.Graph, Start, depth, vertices, edges);
    private EntityRef Vertex(string id) => new(Seed.TargetPartition, GraphIncomingClusterScenario.Collection, id);
    private static EdgeRecord Edge(string id, EntityRef from, EntityRef to) => new(id, from, to, "cycle", "{}", 1L);
}
