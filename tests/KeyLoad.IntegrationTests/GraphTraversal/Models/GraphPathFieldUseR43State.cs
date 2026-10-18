using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal sealed class GraphPathFieldUseR43State(GraphPathClusterSeed seed)
{
    internal GraphPathClusterSeed Seed { get; } = seed;
    internal McpPersistedIdentity Identity { get; set; } = seed.Reader;
    internal bool Allowed
    {
        get; set;
    }
    internal long AllowedEpoch { get; set; } = 2L;
    internal EntityRef Source => Ref(GraphPathClusterScenario.CollectionA, "r43-field-s");
    internal EntityRef Middle => Ref(GraphPathClusterScenario.CollectionA, "r43-field-m");
    internal EntityRef Target => Ref(GraphPathClusterScenario.CollectionB, "r43-field-t");
    internal EntityRef[] Vertices => [Source, Middle, Target];
    internal EdgeRecord[] Edges =>
    [
        new("r43-field-0", Source, Middle, GraphPathClusterScenario.Label, GraphPathClusterScenario.PublicAttributes, 1L),
        new("r43-field-1", Middle, Target, GraphPathClusterScenario.Label, GraphPathClusterScenario.PublicAttributes, 1L)
    ];
    internal EdgeRecord[] ProjectedEdges =>
    [
        new("r43-field-0", Source, Middle, GraphPathClusterScenario.Label, "{\"public\":\"visible\"}", 1L),
        new("r43-field-1", Middle, Target, GraphPathClusterScenario.Label, "{\"public\":\"visible\"}", 1L)
    ];
    internal CommandRequest Command { get; set; } = null!;
    internal CommitReceipt Receipt { get; set; } = null!;
    internal OutboxStatus Outbox { get; set; } = null!;
    internal GraphShortestPathRequest Request(bool labeled)
        => GraphPathClusterScenario.Request(Seed, Source, Target, 2, 3, 2,
            labeled ? ImmutableArray.Create(GraphPathClusterScenario.Label) : null);
    internal SqlGraphPathRequest Sql(bool labeled)
        => GraphPathClusterScenario.SqlRequest(Seed, Source, Target, 2, 3, 2,
            labeled ? GraphPathClusterScenario.Label : null);
    private EntityRef Ref(string collection, string id) => new(Seed.Partition, collection, id);
}
