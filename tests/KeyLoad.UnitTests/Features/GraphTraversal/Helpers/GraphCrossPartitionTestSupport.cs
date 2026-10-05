using System.Collections.Immutable;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphCrossPartitionTestSupport
{
    internal const string Graph = "cross-partition-graph";
    internal const string Collection = "vertices";
    internal const string Root = "root";
    internal const string EmptyJson = "{}";
    private static readonly Guid ShardId = new("274fa47e-69b8-45de-9a78-3e5a7dc37b11");
    private static readonly Guid Incarnation = new("ec738bb7-a2e7-42a8-a3b8-027512aa4f18");
    private static readonly ImmutableArray<string> Voters = ["unit-voter-1", "unit-voter-2", "unit-voter-3"];

    internal static PartitionRef Partition(string key)
        => new("tenant", "database", "orders", key);

    internal static TestDatabase CreateDatabase(DatabaseLimits? limits = null,
        SensitiveFieldPolicy[]? graphFields = null)
    {
        var database = new TestDatabase(limits);
        database.Configure(Collection, ResourceKind.Collection);
        database.Configure(Graph, ResourceKind.Graph, fields: graphFields);
        BootstrapOwner(database);
        return database;
    }

    internal static EntityRef Vertex(PartitionRef partition, string id)
        => new(partition, Collection, id);

    internal static void SeedVertices(TestDatabase database, params EntityRef[] vertices)
    {
        foreach (var group in vertices.GroupBy(vertex => vertex.Partition))
        {
            Commit(database, group.Key, group.Select(vertex => (Mutation)new PutDocument(
                vertex.Collection, vertex.Id, EmptyJson)).ToArray());
        }
    }

    internal static CommitReceipt Commit(TestDatabase database, PartitionRef partition,
        params Mutation[] mutations)
    {
        var commandId = Guid.NewGuid();
        var request = new CommandRequest(commandId, partition, [.. mutations]);
        return database.Submit(OperationKind.Batch, request, id: commandId).Get<CommitReceipt>();
    }

    internal static GraphCrossPartitionDeliveryIntentV1? Intent(TestDatabase database,
        PartitionRef source, string edgeId, EntityRef target)
        => database.Store.Read(view => view.GetRecord<GraphCrossPartitionDeliveryIntentV1>(
            GraphCrossPartitionKeys.Intent(source, Graph, edgeId, target)));

    internal static GraphCrossPartitionReceiverStateV1? Receiver(TestDatabase database,
        PartitionRef target, PartitionRef source, string edgeId, EntityRef destination)
        => database.Store.Read(view => view.GetRecord<GraphCrossPartitionReceiverStateV1>(
            GraphCrossPartitionKeys.Reverse(target, Graph, destination, source, edgeId)));

    internal static GraphCrossPartitionCapacityV1? Capacity(TestDatabase database,
        PartitionRef partition, GraphCrossPartitionCapacityDirection direction)
        => database.Store.Read(view => view.GetRecord<GraphCrossPartitionCapacityV1>(
            GraphCrossPartitionKeys.Capacity(partition, direction)));

    internal static void Deliver(TestDatabase database, PartitionRef source,
        EntityRef destination, string edgeId, long revision)
    {
        Commit(database, destination.Partition,
            new ApplyCrossPartitionReverseEdge(source, Graph, edgeId, destination, revision));
        Commit(database, source,
            new CompleteCrossPartitionReverseEdge(source, Graph, edgeId, destination, revision));
    }

    internal static GraphIncomingEdgesPageV1 ReadIncoming(TestDatabase database,
        EntityRef target, int limit = 10, string principal = Root)
        => database.Database.ReadIncomingGraphEdges(principal,
            new ReadIncomingGraphEdgesRequestV1(1, target, Graph, limit));

    internal static PrincipalRecord CreateRowRestrictedReader(TestDatabase database, string id)
    {
        var grants = ImmutableArray.Create(
            new ScopeGrant("database", Collection, Capability.DocumentsRead),
            new ScopeGrant("database", Graph, Capability.GraphRead));
        var principal = new PrincipalRecord(id, "tenant", grants, [])
        {
            OwnerId = id,
            RestrictRows = true,
            PolicyEpoch = 1
        };
        return database.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
    }

    private static void BootstrapOwner(TestDatabase database)
    {
        var request = new BootstrapPhysicalShardCatalogRequest(1, 0, ShardId, Incarnation, Voters);
        var id = PhysicalShardCatalogIdentity.CreateBootstrapCommandId(ShardId);
        if (!database.Submit(OperationKind.BootstrapPhysicalShardCatalog, request, id: id).Get<bool>())
        {
            throw new InvalidOperationException("The graph fixture did not bootstrap its physical owner.");
        }
    }
}
