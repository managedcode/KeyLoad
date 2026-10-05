namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphIncomingPrivacyTests
{
    private const string MachineKeyHiddenTarget = "hidden-target";
    private const string MachineKeyPrivateSource = "private-source";
    private const string MachineKeyReaderTarget = "reader-target";
    private const string MachineKeyVisibleSource = "visible-source";
    private const string Writer = "row-writer";
    private const string Reader = "incoming-reader";
    private const string EdgeId = "private-edge";
    private const string Label = "links";

    [Test]
    public async Task HiddenSourceEndpointIsOmittedFromIncomingProjection()
    {
        using var database = GraphCrossPartitionTestSupport.CreateDatabase();
        var sourcePartition = GraphCrossPartitionTestSupport.Partition(MachineKeyPrivateSource);
        var targetPartition = GraphCrossPartitionTestSupport.Partition(MachineKeyReaderTarget);
        var source = GraphCrossPartitionTestSupport.Vertex(sourcePartition, "source");
        var target = GraphCrossPartitionTestSupport.Vertex(targetPartition, "target");
        GraphCrossPartitionTestSupport.Commit(database, sourcePartition,
            new PutDocument(source.Collection, source.Id, GraphCrossPartitionTestSupport.EmptyJson,
                Access: new RowAccess(Writer)));
        GraphCrossPartitionTestSupport.Commit(database, targetPartition,
            new PutDocument(target.Collection, target.Id, GraphCrossPartitionTestSupport.EmptyJson,
                Access: new RowAccess(Reader)));
        GraphCrossPartitionTestSupport.Commit(database, sourcePartition,
            new UpsertEdge(GraphCrossPartitionTestSupport.Graph, EdgeId, source, target, Label));
        GraphCrossPartitionTestSupport.Deliver(database, sourcePartition, target, EdgeId, 1);
        GraphCrossPartitionTestSupport.CreateRowRestrictedReader(database, Reader);

        var page = GraphCrossPartitionTestSupport.ReadIncoming(database, target, principal: Reader);

        await Assert.That(page.Rows.IsEmpty).IsTrue();
    }

    [Test]
    public async Task HiddenTargetEndpointIsNotFoundWithoutReturningProjectionRows()
    {
        using var database = GraphCrossPartitionTestSupport.CreateDatabase();
        var sourcePartition = GraphCrossPartitionTestSupport.Partition(MachineKeyVisibleSource);
        var targetPartition = GraphCrossPartitionTestSupport.Partition(MachineKeyHiddenTarget);
        var source = GraphCrossPartitionTestSupport.Vertex(sourcePartition, "source");
        var target = GraphCrossPartitionTestSupport.Vertex(targetPartition, "target");
        GraphCrossPartitionTestSupport.SeedVertices(database, source);
        GraphCrossPartitionTestSupport.Commit(database, targetPartition,
            new PutDocument(target.Collection, target.Id, GraphCrossPartitionTestSupport.EmptyJson,
                Access: new RowAccess(Writer)));
        GraphCrossPartitionTestSupport.Commit(database, sourcePartition,
            new UpsertEdge(GraphCrossPartitionTestSupport.Graph, EdgeId, source, target, Label));
        GraphCrossPartitionTestSupport.Deliver(database, sourcePartition, target, EdgeId, 1);
        GraphCrossPartitionTestSupport.CreateRowRestrictedReader(database, Reader);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            GraphCrossPartitionTestSupport.ReadIncoming(database, target, principal: Reader));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.NotFound);
    }
}
