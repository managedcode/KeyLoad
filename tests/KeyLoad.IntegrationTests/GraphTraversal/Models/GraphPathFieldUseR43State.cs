using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal sealed class GraphPathFieldUseR43State(GraphPathRf3Seed seed)
{
    internal GraphPathRf3Seed Seed { get; } = seed;
    internal McpPersistedIdentity Identity { get; set; } = seed.Reader;
    internal bool Allowed
    {
        get; set;
    }
    internal long AllowedEpoch { get; set; } = 2L;
    internal EntityRef Source => Ref(GraphPathRf3Scenario.CollectionA, "r43-field-s");
    internal EntityRef Middle => Ref(GraphPathRf3Scenario.CollectionA, "r43-field-m");
    internal EntityRef Target => Ref(GraphPathRf3Scenario.CollectionB, "r43-field-t");
    internal EntityRef[] Vertices => [Source, Middle, Target];
    internal EdgeRecord[] Edges =>
    [
        new("r43-field-0", Source, Middle, GraphPathRf3Scenario.Label, GraphPathRf3Scenario.PublicAttributes, 1L),
        new("r43-field-1", Middle, Target, GraphPathRf3Scenario.Label, GraphPathRf3Scenario.PublicAttributes, 1L)
    ];
    internal EdgeRecord[] ProjectedEdges =>
    [
        new("r43-field-0", Source, Middle, GraphPathRf3Scenario.Label, "{\"public\":\"visible\"}", 1L),
        new("r43-field-1", Middle, Target, GraphPathRf3Scenario.Label, "{\"public\":\"visible\"}", 1L)
    ];
    internal AtomicPartitionPlacementResolution Placement { get; set; } = null!;
    internal CommandRequest Command { get; set; } = null!;
    internal CommitReceipt Receipt { get; set; } = null!;
    internal OutboxStatus Outbox { get; set; } = null!;
    internal GraphShortestPathRequest Request(bool labeled)
        => GraphPathRf3Scenario.Request(Seed, Source, Target, 2, 3, 2,
            labeled ? ImmutableArray.Create(GraphPathRf3Scenario.Label) : null);
    internal SqlGraphPathRequest Sql(bool labeled)
        => GraphPathRf3Scenario.SqlRequest(Seed, Source, Target, 2, 3, 2,
            labeled ? GraphPathRf3Scenario.Label : null);
    private EntityRef Ref(string collection, string id) => new(Seed.Partition, collection, id);
}
