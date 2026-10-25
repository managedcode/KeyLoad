using System.Collections.Immutable;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.GraphTraversal;

internal sealed class GraphPathRawReadR43State(GraphPathClusterSeed seed)
{
    internal GraphPathClusterSeed Seed { get; } = seed;
    internal McpPersistedIdentity Identity { get; set; } = seed.Reader;
    internal bool Allowed
    {
        get; set;
    }
    internal EntityRef Source => Ref(GraphPathClusterScenario.CollectionA, "r43-raw-s");
    internal EntityRef Middle => Ref(GraphPathClusterScenario.CollectionA, "r43-raw-m");
    internal EntityRef Target => Ref(GraphPathClusterScenario.CollectionB, "r43-raw-t");
    internal EntityRef[] Vertices => [Source, Middle, Target];
    internal EntityRef[] StoredVertices => Seed.StoredVertices(Vertices);
    internal EdgeRecord[] Edges =>
    [
        new("r43-raw-0", Source, Middle, GraphPathClusterScenario.Label, GraphPathClusterScenario.PublicAttributes, 1L),
        new("r43-raw-1", Middle, Target, GraphPathClusterScenario.Label, GraphPathClusterScenario.PublicAttributes, 1L)
    ];
    internal EdgeRecord[] RedactedEdges =>
    [
        new("r43-raw-0", Source, Middle, GraphPathClusterScenario.Label, "{\"public\":\"visible\"}", 1L),
        new("r43-raw-1", Middle, Target, GraphPathClusterScenario.Label, "{\"public\":\"visible\"}", 1L)
    ];
    internal EdgeRecord[] ProjectedEdges => Allowed
        ? [.. Edges.Select(edge => edge with { AttributesJson = GraphPathClusterScenario.CanonicalAttributes })]
        : RedactedEdges;
    internal EdgeRecord[] StoredProjectedEdges => [.. ProjectedEdges.Select(Seed.StoredEdge)];
    internal CommandRequest Command { get; set; } = null!;
    internal CommitReceipt Receipt { get; set; } = null!;
    internal OutboxStatus Outbox { get; set; } = null!;
    internal GraphShortestPathRequest Request()
        => GraphPathClusterScenario.Request(Seed, Source, Target, 2, 3, 2,
            ImmutableArray.Create(GraphPathClusterScenario.Label));
    internal SqlGraphPathRequest Sql()
        => GraphPathClusterScenario.SqlRequest(Seed, Source, Target, 2, 3, 2,
            GraphPathClusterScenario.Label);
    private EntityRef Ref(string collection, string id) => new(Seed.Partition, collection, id);
}
