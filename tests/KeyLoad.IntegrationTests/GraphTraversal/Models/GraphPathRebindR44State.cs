using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal sealed class GraphPathRebindR44State(GraphPathClusterSeed seed, McpPersistedIdentity identity)
{
    internal GraphPathClusterSeed Seed { get; } = seed;
    internal McpPersistedIdentity Identity { get; } = identity;
    internal int Stage { get; set; } = 1;
    internal EntityRef S => Ref("r44-rebind-s");
    internal EntityRef A => Ref("r44-rebind-a");
    internal EntityRef B => Ref("r44-rebind-b");
    internal EntityRef T => new(Seed.Partition, GraphPathClusterScenario.CollectionB, "r44-rebind-t");
    internal EntityRef[] Vertices => [A, B, S, T];
    internal EdgeRecord[] Edges =>
    [
        new("0-sa", Stage == 1 ? S : B, Stage == 3 ? T : A, GraphPathClusterScenario.Label, "{}", Stage),
        new("1-sb", S, B, GraphPathClusterScenario.Label, "{}", 1L),
        new("2-at", A, T, GraphPathClusterScenario.Label, "{}", 1L),
        new("3-bt", B, T, GraphPathClusterScenario.Label, "{}", 1L)
    ];
    internal EntityRef[] TargetPath => Stage == 1 ? [S, A, T] : [S, B, T];
    internal EdgeRecord[] TargetEdges => Stage switch
    {
        1 => [Edges[0], Edges[2]],
        2 => [Edges[1], Edges[3]],
        _ => [Edges[1], Edges[0]]
    };
    internal EntityRef[] BranchPath => Stage == 1 ? [S, A] : Stage == 2 ? [S, B, A] : [];
    internal EdgeRecord[] BranchEdges => Stage == 1 ? [Edges[0]] : Stage == 2 ? [Edges[1], Edges[0]] : [];
    internal int VertexCap => Stage == 3 ? 3 : 4;
    internal int EdgeCap => Stage == 3 ? 2 : 3;
    internal OutboxStatus Outbox { get; set; } = null!;
    internal List<(CommandRequest Command, CommitReceipt Receipt)> Originals { get; } = [];
    internal CommandRequest Stale { get; set; } = null!;
    internal GraphShortestPathRequest Request(EntityRef target)
        => GraphPathClusterScenario.Request(Seed, S, target, 2, VertexCap, target == A && Stage == 3 ? 3 : EdgeCap,
            ImmutableArray.Create(GraphPathClusterScenario.Label));
    internal SqlGraphPathRequest Sql(EntityRef target)
        => GraphPathClusterScenario.SqlRequest(Seed, S, target, 2, VertexCap, target == A && Stage == 3 ? 3 : EdgeCap, GraphPathClusterScenario.Label);
    internal global::KeyLoad.GraphTraversal Outgoing(EntityRef source)
    {
        if (source == S)
        {
            return Stage == 1 ? new([A, B, S], [Edges[0], Edges[1]]) : new([B, S], [Edges[1]]);
        }
        if (source == A)
        {
            return new([A, T], [Edges[2]]);
        }
        if (source == B)
        {
            return Stage == 1 ? new([B, T], [Edges[3]])
            : Stage == 2 ? new([A, B, T], [Edges[0], Edges[3]]) : new([B, T], [Edges[0], Edges[3]]);
        }
        return new([T], []);
    }
    private EntityRef Ref(string id) => new(Seed.Partition, GraphPathClusterScenario.CollectionA, id);
}
