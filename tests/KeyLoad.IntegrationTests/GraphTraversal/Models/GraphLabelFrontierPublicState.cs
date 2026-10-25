using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.GraphTraversal;

internal sealed class GraphLabelFrontierPublicState(GraphIncomingClusterSeed seed)
{
    internal GraphIncomingClusterSeed Seed { get; } = seed;
    internal EntityRef Start => Vertex("r43-s");
    internal EntityRef A => Vertex("r43-a");
    internal EntityRef B => Vertex("r43-b");
    internal EntityRef Target => Vertex("r43-t");
    internal EntityRef[] Vertices => [A, B, Start, Target];
    internal bool Replaced
    {
        get; set;
    }
    internal OutboxStatus Outbox { get; set; } = null!;
    internal List<(CommandRequest Command, CommitReceipt Receipt)> Originals { get; } = [];
    internal EdgeRecord[] Edges =>
    [
        new("r43-0-sa", Start, A, Replaced ? "keep" : "blocked", "{}", Replaced ? 2L : 1L),
        new("r43-1-sb", Start, B, "keep", "{}", 1L),
        new("r43-2-at", A, Target, "keep", "{}", 1L),
        new("r43-3-bt", B, Target, "keep", "{}", 1L),
        new("r43-4-ts", Target, Start, "blocked", "{}", 1L)
    ];

    internal global::KeyLoad.GraphTraversal Expected(int depth, int profile)
    {
        if (profile >= 5 || (profile == 4 && Replaced))
        {
            return Seed.StoredTraversal(new([Start], []));
        }
        return Seed.StoredTraversal(new([.. ExpectedVertices(depth, profile)],
            [.. ExpectedEdgeIndices(depth, profile).Select(index => Edges[index])]));
    }

    private EntityRef[] ExpectedVertices(int depth, int profile)
    {
        if (profile <= 1 || (profile is 2 or 3 && Replaced))
        {
            return depth == 1 ? [A, B, Start] : [A, B, Start, Target];
        }
        if (profile == 4)
        {
            return [A, Start];
        }
        return depth == 1 ? [B, Start] : [B, Start, Target];
    }

    private int[] ExpectedEdgeIndices(int depth, int profile)
    {
        if (profile <= 1)
        {
            return AllEdgeIndices(depth);
        }
        if (profile == 4)
        {
            return [0];
        }
        if (Replaced)
        {
            return depth == 1 ? [0, 1] : [0, 1, 2, 3];
        }
        return depth == 1 ? [1] : [1, 3];
    }

    private static int[] AllEdgeIndices(int depth)
    {
        if (depth == 1)
        {
            return [0, 1];
        }
        return depth == 2 ? [0, 1, 2, 3] : [0, 1, 2, 3, 4];
    }

    internal int Candidates(int depth, int profile)
    {
        if (profile >= 5 || (profile == 4 && Replaced))
        {
            return 2;
        }
        if (profile == 4)
        {
            return depth == 1 ? 2 : 3;
        }
        if (profile <= 1 || Replaced)
        {
            return AllCandidates(depth);
        }
        return depth + 1;
    }

    private static int AllCandidates(int depth)
    {
        if (depth == 1)
        {
            return 2;
        }
        return depth == 2 ? 4 : 5;
    }
    internal TraverseRequest Request(int depth, int profile, int vertices, int edges)
        => new(Seed.TargetPartition, Seed.Graph, Start, depth, vertices, edges, (Labels(profile)).Value);
    private static global::ManagedCode.Communication.Result<ImmutableArray<string>?> Labels(int profile) => profile switch
    {
        0 => global::ManagedCode.Communication.Result<ImmutableArray<string>?>.FailNotFound(nameof(Labels)),
        1 => global::ManagedCode.Communication.Result<ImmutableArray<string>?>.Succeed(((ImmutableArray<string>?)([]))),
        2 => global::ManagedCode.Communication.Result<ImmutableArray<string>?>.Succeed(((ImmutableArray<string>?)(["keep"]))),
        3 => global::ManagedCode.Communication.Result<ImmutableArray<string>?>.Succeed(((ImmutableArray<string>?)(["keep", "keep"]))),
        4 => global::ManagedCode.Communication.Result<ImmutableArray<string>?>.Succeed(((ImmutableArray<string>?)(["blocked"]))),
        5 => global::ManagedCode.Communication.Result<ImmutableArray<string>?>.Succeed(((ImmutableArray<string>?)(["unknown"]))),
        6 => global::ManagedCode.Communication.Result<ImmutableArray<string>?>.Succeed(((ImmutableArray<string>?)(["KEEP"]))),
        _ => throw new ArgumentOutOfRangeException(nameof(profile))
    };
    private EntityRef Vertex(string id) => new(Seed.TargetPartition, GraphIncomingClusterScenario.Collection, id);
}
