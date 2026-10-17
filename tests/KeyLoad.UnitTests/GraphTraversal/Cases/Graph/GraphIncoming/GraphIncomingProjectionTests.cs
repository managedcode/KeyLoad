namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphIncomingProjectionTests
{
    private const string MachineKeyLocal = "local";
    private const string MachineKeyRemote = "remote";
    private const string Reader = "projection-reader";
    private const string EdgeId = "projection-edge";
    private const string Label = "links";
    private const string Secret = "never-project-this-value";
    private const string Public = "public-edge-value";
    private const string SecretPath = "/secret";
    private const string EmptyJson = GraphCrossPartitionTestSupport.EmptyJson;

    [Test]
    public async Task LocalCanonicalIncomingEdgeUsesZeroDeliveryRevision()
    {
        using var database = GraphCrossPartitionTestSupport.CreateDatabase();
        var partition = GraphCrossPartitionTestSupport.Partition(MachineKeyLocal);
        var from = GraphCrossPartitionTestSupport.Vertex(partition, "from");
        var target = GraphCrossPartitionTestSupport.Vertex(partition, "target");
        GraphCrossPartitionTestSupport.SeedVertices(database, from, target);
        GraphCrossPartitionTestSupport.Commit(database, partition,
            new UpsertEdge(GraphCrossPartitionTestSupport.Graph, EdgeId, from, target, Label));

        var page = GraphCrossPartitionTestSupport.ReadIncoming(database, target);

        await Assert.That(page.Rows.Length).IsEqualTo(1);
        await Assert.That(page.Rows[0].DeliveredRevision).IsEqualTo(0L);
        await Assert.That(page.Rows[0].Edge.Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task MixedLocalAndCrossRowsProjectProtectedFieldsBeforeRetention()
    {
        var policies = new[] { new SensitiveFieldPolicy(SecretPath, "private") };
        using var database = GraphCrossPartitionTestSupport.CreateDatabase(graphFields: policies);
        var localPartition = GraphCrossPartitionTestSupport.Partition(MachineKeyLocal);
        var remotePartition = GraphCrossPartitionTestSupport.Partition(MachineKeyRemote);
        var localFrom = GraphCrossPartitionTestSupport.Vertex(localPartition, "local-from");
        var target = GraphCrossPartitionTestSupport.Vertex(localPartition, "target");
        var remoteFrom = GraphCrossPartitionTestSupport.Vertex(remotePartition, "remote-from");
        SeedOwned(database, localFrom, target, remoteFrom);
        GraphCrossPartitionTestSupport.Commit(database, localPartition,
            Edge(localFrom, target));
        GraphCrossPartitionTestSupport.Commit(database, remotePartition,
            Edge(remoteFrom, target));
        GraphCrossPartitionTestSupport.Deliver(database, remotePartition, target, EdgeId, 1);
        GraphCrossPartitionTestSupport.CreateRowRestrictedReader(database, Reader);

        var page = GraphCrossPartitionTestSupport.ReadIncoming(database, target, principal: Reader);

        await Assert.That(page.Rows.Length).IsEqualTo(2);
        await Assert.That(page.Rows.All(row => row.Edge.AttributesJson.Contains(Public, StringComparison.Ordinal))).IsTrue();
        await Assert.That(page.Rows.All(row => !row.Edge.AttributesJson.Contains(Secret, StringComparison.Ordinal))).IsTrue();
        await Assert.That(page.Rows.Select(row => row.DeliveredRevision).SequenceEqual([0L, 1L])).IsTrue();
    }

    private static UpsertEdge Edge(EntityRef from, EntityRef to)
        => new(GraphCrossPartitionTestSupport.Graph, EdgeId, from, to, Label,
            $"{{\"secret\":\"{Secret}\",\"public\":\"{Public}\"}}");

    private static void SeedOwned(TestDatabase database, params EntityRef[] vertices)
    {
        foreach (var group in vertices.GroupBy(vertex => vertex.Partition))
        {
            GraphCrossPartitionTestSupport.Commit(database, group.Key,
                group.Select(vertex => (Mutation)new PutDocument(vertex.Collection, vertex.Id,
                    EmptyJson, Access: new RowAccess(Reader))).ToArray());
        }
    }
}
