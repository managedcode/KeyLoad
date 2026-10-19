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

    internal static PartitionRef Partition(string key)
        => new("tenant", "database", "orders", key);

    internal static TestDatabase CreateDatabase(DatabaseLimits? limits = null,
        SensitiveFieldPolicy[]? graphFields = null)
    {
        var database = new TestDatabase(limits);
        database.Configure(Collection, ResourceKind.Collection);
        database.Configure(Graph, ResourceKind.Graph, fields: graphFields);
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

    internal static GraphCrossPartitionDeliveryIntent? Intent(TestDatabase database,
        PartitionRef source, string edgeId, EntityRef target)
        => database.Store.Read(view => view.GetRecord<GraphCrossPartitionDeliveryIntent>(
            GraphCrossPartitionKeys.Intent(source, Graph, edgeId, target)));

    internal static GraphCrossPartitionReceiverState? Receiver(TestDatabase database,
        PartitionRef target, PartitionRef source, string edgeId, EntityRef destination)
        => database.Store.Read(view => view.GetRecord<GraphCrossPartitionReceiverState>(
            GraphCrossPartitionKeys.Reverse(target, Graph, destination, source, edgeId)));

    internal static GraphCrossPartitionCapacity? Capacity(TestDatabase database,
        PartitionRef partition, GraphCrossPartitionCapacityDirection direction)
        => database.Store.Read(view => view.GetRecord<GraphCrossPartitionCapacity>(
            GraphCrossPartitionKeys.Capacity(partition, direction)));

    internal static void Deliver(TestDatabase database, PartitionRef source,
        EntityRef destination, string edgeId, long revision)
    {
        Commit(database, destination.Partition,
            new ApplyCrossPartitionReverseEdge(source, Graph, edgeId, destination, revision));
        Commit(database, source,
            new CompleteCrossPartitionReverseEdge(source, Graph, edgeId, destination, revision));
    }

    internal static GraphIncomingEdgesPage ReadIncoming(TestDatabase database,
        EntityRef target, int limit = 10, string principal = Root)
        => database.Database.ReadIncomingGraphEdges(principal,
            new ReadIncomingGraphEdgesRequest(1, target, Graph, limit));

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

}
