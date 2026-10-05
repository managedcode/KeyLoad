namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphCrossPartitionRepairTests
{
    private const string EdgeId = "edge-1";
    private const string Label = "links";

    [Test]
    public async Task CoalescedMultiHopRepairTombstonesEveryPreviouslyDeliveredDestination()
    {
        using var database = GraphCrossPartitionTestSupport.CreateDatabase();
        var source = GraphCrossPartitionTestSupport.Partition("source");
        var first = GraphCrossPartitionTestSupport.Vertex(GraphCrossPartitionTestSupport.Partition("first"), "v");
        var second = GraphCrossPartitionTestSupport.Vertex(GraphCrossPartitionTestSupport.Partition("second"), "v");
        var third = GraphCrossPartitionTestSupport.Vertex(GraphCrossPartitionTestSupport.Partition("third"), "v");
        var from = GraphCrossPartitionTestSupport.Vertex(source, "from");
        GraphCrossPartitionTestSupport.SeedVertices(database, from, first, second, third);

        GraphCrossPartitionTestSupport.Commit(database, source, Edge(from, first));
        GraphCrossPartitionTestSupport.Deliver(database, source, first, EdgeId, 1);
        Upsert(database, source, from, second, 1);
        Upsert(database, source, from, third, 2);

        foreach (var oldTarget in new[] { first, second })
        {
            var intent = GraphCrossPartitionTestSupport.Intent(database, source, EdgeId, oldTarget);
            await Assert.That(intent).IsNotNull();
            await Assert.That(intent!.Revision).IsEqualTo(3L);
            await Assert.That(intent.Deleted).IsTrue();
        }
        var current = GraphCrossPartitionTestSupport.Intent(database, source, EdgeId, third);
        await Assert.That(current).IsNotNull();
        await Assert.That(current!.Revision).IsEqualTo(3L);
        await Assert.That(current.Deleted).IsFalse();

        GraphCrossPartitionTestSupport.Deliver(database, source, first, EdgeId, 3);
        GraphCrossPartitionTestSupport.Deliver(database, source, second, EdgeId, 3);
        GraphCrossPartitionTestSupport.Deliver(database, source, third, EdgeId, 3);
        GraphCrossPartitionTestSupport.Commit(database, first.Partition,
            new ApplyCrossPartitionReverseEdge(source, GraphCrossPartitionTestSupport.Graph,
                EdgeId, first, 1));

        foreach (var retired in new[] { first, second })
        {
            var state = GraphCrossPartitionTestSupport.Receiver(database, retired.Partition,
                source, EdgeId, retired);
            await Assert.That(state!.Deleted).IsTrue();
            await Assert.That(GraphCrossPartitionTestSupport.ReadIncoming(database, retired).Rows.IsEmpty).IsTrue();
            await Assert.That(GraphCrossPartitionTestSupport.Intent(database, source, EdgeId, retired)).IsNull();
        }
        var active = GraphCrossPartitionTestSupport.ReadIncoming(database, third);
        await Assert.That(active.Rows.Select(row => row.Edge.Id).SequenceEqual([EdgeId])).IsTrue();
        await Assert.That(GraphCrossPartitionTestSupport.Intent(database, source, EdgeId, third)).IsNull();
    }

    private static UpsertEdge Edge(EntityRef from, EntityRef to)
        => new(GraphCrossPartitionTestSupport.Graph, EdgeId, from, to, Label);

    private static void Upsert(TestDatabase database, PartitionRef source, EntityRef from,
        EntityRef to, long expectedRevision)
        => GraphCrossPartitionTestSupport.Commit(database, source,
            new UpsertEdge(GraphCrossPartitionTestSupport.Graph, EdgeId, from, to, Label,
                ExpectedRevision: expectedRevision));
}
