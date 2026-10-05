using KeyLoad.Core;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphCrossPartitionReceiverTests
{
    private const string EdgeId = "receiver-edge";
    private const string Label = "links";
    private const string ConflictingFingerprint = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";

    [Test]
    public async Task DuplicateTargetApplyIsAnIdempotentNativeNoOp()
    {
        using var database = GraphCrossPartitionTestSupport.CreateDatabase();
        var source = GraphCrossPartitionTestSupport.Partition("source");
        var target = GraphCrossPartitionTestSupport.Vertex(GraphCrossPartitionTestSupport.Partition("target"), "to");
        var from = GraphCrossPartitionTestSupport.Vertex(source, "from");
        GraphCrossPartitionTestSupport.SeedVertices(database, from, target);
        GraphCrossPartitionTestSupport.Commit(database, source,
            new UpsertEdge(GraphCrossPartitionTestSupport.Graph, EdgeId, from, target, Label));
        var request = new ApplyCrossPartitionReverseEdge(source,
            GraphCrossPartitionTestSupport.Graph, EdgeId, target, 1);
        GraphCrossPartitionTestSupport.Commit(database, target.Partition, request);
        var before = GraphCrossPartitionTestSupport.Receiver(database, target.Partition,
            source, EdgeId, target);

        GraphCrossPartitionTestSupport.Commit(database, target.Partition, request);
        var after = GraphCrossPartitionTestSupport.Receiver(database, target.Partition,
            source, EdgeId, target);

        await Assert.That(after).IsEqualTo(before);
        await Assert.That(GraphCrossPartitionTestSupport.ReadIncoming(database, target).Rows.Length).IsEqualTo(1);
    }

    [Test]
    public async Task EqualRevisionDifferentFingerprintFailsConflictWithoutReplacingReceiver()
    {
        using var database = GraphCrossPartitionTestSupport.CreateDatabase();
        var source = GraphCrossPartitionTestSupport.Partition("source");
        var target = GraphCrossPartitionTestSupport.Vertex(GraphCrossPartitionTestSupport.Partition("target"), "to");
        var from = GraphCrossPartitionTestSupport.Vertex(source, "from");
        GraphCrossPartitionTestSupport.SeedVertices(database, from, target);
        GraphCrossPartitionTestSupport.Commit(database, source,
            new UpsertEdge(GraphCrossPartitionTestSupport.Graph, EdgeId, from, target, Label));
        var key = GraphCrossPartitionKeys.Reverse(target.Partition, GraphCrossPartitionTestSupport.Graph,
            target, source, EdgeId);
        var conflicting = new GraphCrossPartitionReceiverStateV1(1, source,
            GraphCrossPartitionTestSupport.Graph, EdgeId, target, 1, false, ConflictingFingerprint);
        database.Store.Commit((transaction, _) =>
        {
            transaction.PutRecord(key, conflicting);
            return true;
        });

        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            GraphCrossPartitionTestSupport.Commit(database, target.Partition,
                new ApplyCrossPartitionReverseEdge(source, GraphCrossPartitionTestSupport.Graph,
                    EdgeId, target, 1)));
        var after = GraphCrossPartitionTestSupport.Receiver(database, target.Partition, source, EdgeId, target);

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(after).IsEqualTo(conflicting);
    }
}
