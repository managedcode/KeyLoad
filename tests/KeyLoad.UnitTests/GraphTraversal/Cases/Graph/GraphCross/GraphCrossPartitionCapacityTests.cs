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

    private const int Capacity = 16;

    [Test]
    public async Task PendingIntentOneOverCapacityRejectsWholeSourceMutation()
    {
        using var database = GraphCrossPartitionTestSupport.CreateDatabase(
            new DatabaseLimits { MaxScanRecords = Capacity });
        var source = GraphCrossPartitionTestSupport.Partition(MachineKeySource);
        var first = GraphCrossPartitionTestSupport.Vertex(GraphCrossPartitionTestSupport.Partition(MachineKeyFirst), "v");
        var second = GraphCrossPartitionTestSupport.Vertex(GraphCrossPartitionTestSupport.Partition(MachineKeySecond), "v");
        var from = GraphCrossPartitionTestSupport.Vertex(source, "from");
        GraphCrossPartitionTestSupport.SeedVertices(database, from, first, second);
        from = database.ExpectedReference(from);
        first = database.ExpectedReference(first);
        second = database.ExpectedReference(second);
        for (var index = 0; index < Capacity; index++)
        {
            var id = index == 0 ? EdgeId : EdgeId + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            GraphCrossPartitionTestSupport.Commit(database, source,
                new UpsertEdge(GraphCrossPartitionTestSupport.Graph, id, from, first, Label));
        }
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
        await Assert.That(capacity!.RecordCount).IsEqualTo((long)Capacity);
    }
}
