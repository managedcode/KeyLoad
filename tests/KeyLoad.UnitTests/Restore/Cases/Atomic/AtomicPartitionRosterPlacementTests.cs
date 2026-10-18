using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class AtomicPartitionRosterPlacementTests
{
    private const string DocumentJson = "{\"value\":1}";
    private const string VertexId = "vertex";
    private const string EdgeId = "roster-edge";
    private const string EdgeLabel = "links";
    private const long FirstRevision = 1;

    [Test]
    public async Task AcBackup004CrossPartitionDestinationWritesRemainRegisteredAfterDelete()
    {
        using var fixture = new AtomicPartitionRosterFixture();
        fixture.ConfigureGraph();
        var sourceVertex = new EntityRef(AtomicPartitionRosterFixture.Source, AtomicPartitionRosterFixture.Collection, VertexId);
        var destinationVertex = new EntityRef(AtomicPartitionRosterFixture.Destination, AtomicPartitionRosterFixture.Collection, VertexId);
        var sourceSeed = fixture.Batch(AtomicPartitionRosterFixture.Source,
            new PutDocument(AtomicPartitionRosterFixture.Collection, VertexId, DocumentJson));
        var destinationSeed = fixture.Batch(AtomicPartitionRosterFixture.Destination,
            new PutDocument(AtomicPartitionRosterFixture.Collection, VertexId, DocumentJson));
        await Assert.That(Errors.Code(sourceSeed.Problem)).IsNull();
        await Assert.That(Errors.Code(destinationSeed.Problem)).IsNull();
        var targetEntry = fixture.ReadEntry(AtomicPartitionRosterFixture.Destination)!;
        await Assert.That(targetEntry).IsNotNull();
        var edge = fixture.Batch(AtomicPartitionRosterFixture.Source,
            new UpsertEdge(AtomicPartitionRosterFixture.Graph, EdgeId, sourceVertex, destinationVertex, EdgeLabel));
        await Assert.That(Errors.Code(edge.Problem)).IsNull();
        var delivered = fixture.Batch(AtomicPartitionRosterFixture.Destination,
            new ApplyCrossPartitionReverseEdge(AtomicPartitionRosterFixture.Source,
                AtomicPartitionRosterFixture.Graph, EdgeId, destinationVertex, FirstRevision));
        await Assert.That(Errors.Code(delivered.Problem)).IsNull();
        await Assert.That(fixture.ReadEntry(AtomicPartitionRosterFixture.Destination)).IsEqualTo(targetEntry);
        var receiver = fixture.Store.Read(view => view.GetRecord<GraphCrossPartitionReceiverStateV1>(
            GraphCrossPartitionKeys.Reverse(AtomicPartitionRosterFixture.Destination,
                AtomicPartitionRosterFixture.Graph, destinationVertex, AtomicPartitionRosterFixture.Source, EdgeId)));
        await Assert.That(receiver).IsNotNull();
        var completed = fixture.Batch(AtomicPartitionRosterFixture.Source,
            new CompleteCrossPartitionReverseEdge(AtomicPartitionRosterFixture.Source,
                AtomicPartitionRosterFixture.Graph, EdgeId, destinationVertex, FirstRevision));
        await Assert.That(Errors.Code(completed.Problem)).IsNull();
        await Assert.That(fixture.ReadEntry(AtomicPartitionRosterFixture.Destination)).IsEqualTo(targetEntry);

        var sourceEntry = fixture.ReadEntry(AtomicPartitionRosterFixture.Source)!;
        var delete = fixture.Batch(AtomicPartitionRosterFixture.Source,
            new DeleteEdge(AtomicPartitionRosterFixture.Graph, EdgeId, FirstRevision));
        await Assert.That(Errors.Code(delete.Problem)).IsNull();
        await Assert.That(fixture.ReadEntry(AtomicPartitionRosterFixture.Source)).IsEqualTo(sourceEntry);
        await Assert.That(fixture.ReadEntry(AtomicPartitionRosterFixture.Destination)).IsEqualTo(targetEntry);
        fixture.Reopen();
        await Assert.That(fixture.ReadEntry(AtomicPartitionRosterFixture.Destination)).IsEqualTo(targetEntry);
    }

}
