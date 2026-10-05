using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphCrossPartitionCapacityTests
{
    private const string MachineKeyFirst = "first";
    private const string MachineKeySecond = "second";
    private const string MachineKeySource = "source";
    private const string EdgeId = "capacity-edge";
    private const string Label = "links";

    [Test]
    public async Task PendingIntentOneOverCapacityRejectsWholeSourceMutation()
    {
        using var database = GraphCrossPartitionTestSupport.CreateDatabase(
            new DatabaseLimits { MaxScanRecords = 1 });
        var source = GraphCrossPartitionTestSupport.Partition(MachineKeySource);
        var first = GraphCrossPartitionTestSupport.Vertex(GraphCrossPartitionTestSupport.Partition(MachineKeyFirst), "v");
        var second = GraphCrossPartitionTestSupport.Vertex(GraphCrossPartitionTestSupport.Partition(MachineKeySecond), "v");
        var from = GraphCrossPartitionTestSupport.Vertex(source, "from");
        GraphCrossPartitionTestSupport.SeedVertices(database, from, first, second);
        GraphCrossPartitionTestSupport.Commit(database, source,
            new UpsertEdge(GraphCrossPartitionTestSupport.Graph, EdgeId, from, first, Label));
        var oldIntent = GraphCrossPartitionTestSupport.Intent(database, source, EdgeId, first);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            GraphCrossPartitionTestSupport.Commit(database, source,
                new UpsertEdge(GraphCrossPartitionTestSupport.Graph, EdgeId, from, second, Label,
                    ExpectedRevision: 1)));
        var canonical = database.Store.Read(view => view.GetRecord<EdgeRecord>(
            GraphCrossPartitionKeys.CanonicalEdge(source, GraphCrossPartitionTestSupport.Graph, EdgeId)));
        var retainedIntent = GraphCrossPartitionTestSupport.Intent(database, source, EdgeId, first);
        var nextIntent = GraphCrossPartitionTestSupport.Intent(database, source, EdgeId, second);
        var capacity = GraphCrossPartitionTestSupport.Capacity(database, source,
            GraphCrossPartitionCapacityDirection.PendingIntents);

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(canonical!.To).IsEqualTo(first);
        await Assert.That(canonical.Revision).IsEqualTo(1L);
        await Assert.That(retainedIntent).IsEqualTo(oldIntent);
        await Assert.That(nextIntent).IsNull();
        await Assert.That(capacity!.RecordCount).IsEqualTo(1L);
    }
}
