namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphIncomingEdgesTests
{
    private const string MachineKeySourceA = "source-a";
    private const string MachineKeySourceB = "source-b";
    private const string MachineKeyTarget = "target";
    private const string EdgeId = "shared-edge-id";
    private const string Label = "links";

    [Test]
    public async Task IncomingProjectionKeepsDistinctFullSourcePartitionsAndIsBoundedAsOnePage()
    {
        using var database = GraphCrossPartitionTestSupport.CreateDatabase();
        var firstSource = GraphCrossPartitionTestSupport.Partition(MachineKeySourceA);
        var secondSource = GraphCrossPartitionTestSupport.Partition(MachineKeySourceB);
        var target = GraphCrossPartitionTestSupport.Vertex(
            GraphCrossPartitionTestSupport.Partition(MachineKeyTarget), "same-vertex-id");
        var localSource = GraphCrossPartitionTestSupport.Vertex(target.Partition, "local-source");
        var firstFrom = GraphCrossPartitionTestSupport.Vertex(firstSource, "same-vertex-id");
        var secondFrom = GraphCrossPartitionTestSupport.Vertex(secondSource, "same-vertex-id");
        GraphCrossPartitionTestSupport.SeedVertices(database, localSource, firstFrom, secondFrom, target);
        GraphCrossPartitionTestSupport.Commit(database, target.Partition, Edge(localSource, target));
        GraphCrossPartitionTestSupport.Commit(database, firstSource, Edge(firstFrom, target));
        GraphCrossPartitionTestSupport.Commit(database, secondSource, Edge(secondFrom, target));
        GraphCrossPartitionTestSupport.Deliver(database, firstSource, target, EdgeId, 1);
        GraphCrossPartitionTestSupport.Deliver(database, secondSource, target, EdgeId, 1);

        var page = GraphCrossPartitionTestSupport.ReadIncoming(database, target, 3);
        await Assert.That(page.Rows.Length).IsEqualTo(3);
        await Assert.That(page.Rows.Select(row => row.Edge.From.Partition).ToHashSet()
            .SetEquals([target.Partition, firstSource, secondSource])).IsTrue();
        await Assert.That(page.Rows.Select(row => row.DeliveredRevision).SequenceEqual([1L, 1L, 0L])).IsTrue();
        await Assert.That(page.Rows.Select(row => row.Edge.From.Partition.PartitionKey)
            .SequenceEqual([MachineKeySourceA, MachineKeySourceB, MachineKeyTarget])).IsTrue();
        await Assert.That(page.Rows.All(row => row.Edge.Id == EdgeId)).IsTrue();
        await Assert.That(page.Projection).IsEqualTo(GraphCrossPartitionProtocol.EventualProjection);
        await Assert.That(page.CutPosition).IsGreaterThan(0L);

        var failure = Assert.ThrowsExactly<KeyLoadException>(
            () => GraphCrossPartitionTestSupport.ReadIncoming(database, target, 2));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task IncomingProjectionRejectsInvalidShapeBeforeOpeningAReadCut()
    {
        using var database = GraphCrossPartitionTestSupport.CreateDatabase();
        var target = GraphCrossPartitionTestSupport.Vertex(
            GraphCrossPartitionTestSupport.Partition(MachineKeyTarget), "vertex");
        var malformed = new ReadIncomingGraphEdgesRequestV1(2, target,
            GraphCrossPartitionTestSupport.Graph, 1);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            database.Database.ReadIncomingGraphEdges(GraphCrossPartitionTestSupport.Root, malformed));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
    }

    private static UpsertEdge Edge(EntityRef from, EntityRef to)
        => new(GraphCrossPartitionTestSupport.Graph, EdgeId, from, to, Label);
}
