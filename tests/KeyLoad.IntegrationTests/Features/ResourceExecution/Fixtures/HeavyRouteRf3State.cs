using KeyLoad.IntegrationTests.Features.Search;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal sealed record HeavyRouteRf3State(NativeAnnMaintenanceRf3Scenario Ann, AnnMaintenanceRequest Pin,
    CommandRequest Seed, CommitReceipt Receipt, PrincipalRecord Principal, string Key, OutboxStatus Outbox)
{
    internal const string Graph = "admission-graph";
    internal const string Collection = NativeAnnMaintenanceRf3Scenario.Collection;
    internal const string ItemA = "ann-a";
    internal const string ItemB = "ann-b";
    internal const string ItemC = "ann-c";
    internal PartitionRef Partition => Ann.Partition;
    internal EntityRef Vertex(string id) => new(Partition, Collection, id);
    internal EdgeRecord[] Edges => [new("ab", Vertex(ItemA), Vertex(ItemB), "route", "{}", 1L),
        new("bc", Vertex(ItemB), Vertex(ItemC), "route", "{}", 1L)];
    internal GraphShortestPathRequest Path => new(1, Partition, Graph, Vertex(ItemA), Vertex(ItemC), 2, 3, 2);
    internal ReadIncomingGraphEdgesRequestV1 Incoming => new(1, Vertex(ItemC), Graph, 10);
    internal ApproximateSearchRequest Approximate => new(1, Ann.Search(), Pin.Consumer, Pin.IndexGeneration);
    internal GraphSearchRequest Search => new(1, Ann.Search(), Scope: new(new(Graph, [Vertex(ItemA)], 2, 3, 2)));
    internal PrincipalRecord Policy(long epoch, bool allow) => Principal with
    {
        PolicyEpoch = epoch,
        Grants = allow ? [new(Partition.DatabaseId, Collection, Capability.DocumentsRead | Capability.Query | Capability.VectorSearch),
            new(Partition.DatabaseId, Graph, Capability.GraphRead)]
            : [new(Partition.DatabaseId, Collection, Capability.DocumentsRead | Capability.Query)]
    };
}
