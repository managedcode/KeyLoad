using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphCrossPartitionDeleteTests
{
    private const string MachineKeySource = "source";
    private const string MachineKeyTarget = "target";
    private const string EdgeId = "deleted-edge";
    private const string Label = "links";

    [Test]
    public async Task DeleteCommitsOwnerTombstoneAndRemovesDeliveredIncomingEdge()
    {
        using var database = GraphCrossPartitionTestSupport.CreateDatabase();
        var source = GraphCrossPartitionTestSupport.Partition(MachineKeySource);
        var target = GraphCrossPartitionTestSupport.Vertex(GraphCrossPartitionTestSupport.Partition(MachineKeyTarget), "to");
        var from = GraphCrossPartitionTestSupport.Vertex(source, "from");
        GraphCrossPartitionTestSupport.SeedVertices(database, from, target);
        GraphCrossPartitionTestSupport.Commit(database, source,
            new UpsertEdge(GraphCrossPartitionTestSupport.Graph, EdgeId, from, target, Label));
        GraphCrossPartitionTestSupport.Deliver(database, source, target, EdgeId, 1);
        GraphCrossPartitionTestSupport.Commit(database, source,
            new DeleteEdge(GraphCrossPartitionTestSupport.Graph, EdgeId, 1));
        var intent = GraphCrossPartitionTestSupport.Intent(database, source, EdgeId, target);
        await Assert.That(intent!.Deleted).IsTrue();
        await Assert.That(intent.Revision).IsEqualTo(2L);

        GraphCrossPartitionTestSupport.Deliver(database, source, target, EdgeId, 2);
        var owner = database.Store.Read(view => view.GetRecord<GraphEdgeOwnerVersionV1>(
            GraphCrossPartitionKeys.OwnerVersion(source, GraphCrossPartitionTestSupport.Graph, EdgeId)));
        var canonical = database.Store.Read(view => view.GetRecord<EdgeRecord>(
            GraphCrossPartitionKeys.CanonicalEdge(source, GraphCrossPartitionTestSupport.Graph, EdgeId)));
        var receiver = GraphCrossPartitionTestSupport.Receiver(database, target.Partition, source, EdgeId, target);
        GraphCrossPartitionTestSupport.Commit(database, target.Partition,
            new ApplyCrossPartitionReverseEdge(source, GraphCrossPartitionTestSupport.Graph,
                EdgeId, target, 1));

        await Assert.That(owner!.Deleted).IsTrue();
        await Assert.That(owner.Revision).IsEqualTo(2L);
        await Assert.That(canonical).IsNull();
        await Assert.That(receiver!.Deleted).IsTrue();
        await Assert.That(receiver.Revision).IsEqualTo(2L);
        await Assert.That(GraphCrossPartitionTestSupport.ReadIncoming(database, target).Rows.IsEmpty).IsTrue();
        await Assert.That(GraphCrossPartitionTestSupport.Intent(database, source, EdgeId, target)).IsNull();
    }
}
