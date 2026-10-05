using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphCrossPartitionCurrentTargetGuardTests
{
    private const string MachineKeyCurrentSource = "current-source";
    private const string MachineKeyCurrentTarget = "current-target";
    private const string EdgeId = "current-target-edge";
    private const string Label = "links";

    [Test]
    public async Task TombstoneIntentCannotDeleteItsStillCurrentCanonicalDestination()
    {
        using var database = GraphCrossPartitionTestSupport.CreateDatabase();
        var source = GraphCrossPartitionTestSupport.Partition(MachineKeyCurrentSource);
        var target = GraphCrossPartitionTestSupport.Vertex(
            GraphCrossPartitionTestSupport.Partition(MachineKeyCurrentTarget), "current-to");
        var from = GraphCrossPartitionTestSupport.Vertex(source, "current-from");
        GraphCrossPartitionTestSupport.SeedVertices(database, from, target);
        GraphCrossPartitionTestSupport.Commit(database, source,
            new UpsertEdge(GraphCrossPartitionTestSupport.Graph, EdgeId, from, target, Label));
        var edge = new EdgeRecord(EdgeId, from, target, Label,
            GraphCrossPartitionTestSupport.EmptyJson, 1);
        var forgedTombstone = new GraphCrossPartitionDeliveryIntentV1(1, source,
            GraphCrossPartitionTestSupport.Graph, EdgeId, target, 1, true, edge,
            GraphCrossPartitionTestSupport.Root, 0,
            GraphCrossPartitionRecords.Fingerprint(source, GraphCrossPartitionTestSupport.Graph,
                EdgeId, target, 1, true, edge));
        database.Store.Commit((transaction, _) =>
        {
            transaction.PutRecord(GraphCrossPartitionKeys.Intent(forgedTombstone), forgedTombstone);
            return true;
        });

        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            GraphCrossPartitionTestSupport.Commit(database, target.Partition,
                new ApplyCrossPartitionReverseEdge(source, GraphCrossPartitionTestSupport.Graph,
                    EdgeId, target, 1)));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(GraphCrossPartitionTestSupport.Receiver(database, target.Partition,
            source, EdgeId, target)).IsNull();
        await Assert.That(database.Store.Read(view => view.GetRecord<EdgeRecord>(
            GraphCrossPartitionKeys.CanonicalEdge(source, GraphCrossPartitionTestSupport.Graph, EdgeId))))
            .IsEqualTo(edge);
    }
}
