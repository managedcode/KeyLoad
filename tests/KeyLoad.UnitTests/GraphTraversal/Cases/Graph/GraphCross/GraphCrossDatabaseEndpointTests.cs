namespace KeyLoad.UnitTests.GraphTraversal;

internal sealed class GraphCrossDatabaseEndpointTests
{
    private const string TargetDatabase = "target-database";
    private const string SourceKey = "source";
    private const string TargetKey = "target";
    private const string EdgeId = "cross-database-edge";
    private const string Label = "links";

    [Test]
    public async Task EdgeToAnotherDatabaseCommitsDeliversAndDeletes()
    {
        using var database = GraphCrossPartitionTestSupport.CreateDatabase();
        var source = GraphCrossPartitionTestSupport.Partition(SourceKey);
        var targetPartition = new PartitionRef(source.TenantId, TargetDatabase, source.TransactionDomainId, TargetKey);
        ConfigureTargetDatabase(database, targetPartition);
        var from = GraphCrossPartitionTestSupport.Vertex(source, "from");
        var target = GraphCrossPartitionTestSupport.Vertex(targetPartition, "to");
        GraphCrossPartitionTestSupport.SeedVertices(database, from, target);

        var created = GraphCrossPartitionTestSupport.Commit(database, source,
            new UpsertEdge(GraphCrossPartitionTestSupport.Graph, EdgeId, from, target, Label));
        GraphCrossPartitionTestSupport.Deliver(database, source, target, EdgeId, 1);
        var delivered = GraphCrossPartitionTestSupport.ReadIncoming(database, target);

        await Assert.That(created.Mutations[^1].Revision).IsEqualTo(1L);
        await Assert.That(delivered.Rows.Length).IsEqualTo(1);
        await Assert.That(delivered.Rows[0].Edge.Id).IsEqualTo(EdgeId);
        await Assert.That(delivered.Rows[0].Edge.From.Partition).IsEqualTo(source);
        await Assert.That(delivered.Rows[0].DeliveredRevision).IsEqualTo(1L);

        var deleted = GraphCrossPartitionTestSupport.Commit(database, source,
            new DeleteEdge(GraphCrossPartitionTestSupport.Graph, EdgeId, 1));
        GraphCrossPartitionTestSupport.Deliver(database, source, target, EdgeId, 2);

        await Assert.That(deleted.Mutations[^1].Revision).IsEqualTo(2L);
        await Assert.That(GraphCrossPartitionTestSupport.ReadIncoming(database, target).Rows.IsEmpty).IsTrue();
        await Assert.That(GraphCrossPartitionTestSupport.Intent(database, source, EdgeId, target)).IsNull();
    }

    private static void ConfigureTargetDatabase(TestDatabase database, PartitionRef partition)
    {
        var resources = new[] { (GraphCrossPartitionTestSupport.Collection, ResourceKind.Collection), (GraphCrossPartitionTestSupport.Graph, ResourceKind.Graph) };
        foreach (var (name, kind) in resources)
        {
            _ = database.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(partition.TenantId,
                partition.DatabaseId, new ResourceDefinition(name, kind, partition.TransactionDomainId))).Get<ResourceDefinition>();
        }
    }
}
