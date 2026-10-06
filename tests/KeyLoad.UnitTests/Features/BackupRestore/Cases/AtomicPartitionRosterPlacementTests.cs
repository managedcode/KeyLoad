using KeyLoad.Core.Features.ClusterRouting.Serialization;
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
    private const int PlacementVersion = 1;
    private const long InitialPlacementRevision = 0;
    private const string EmptyPlacementPartitionKey = "empty-placement";
    private const string ShardIdText = "11223344-5566-7788-99aa-bbccddeeff00";
    private static readonly Guid ShardId = Guid.Parse(ShardIdText);

    [Test]
    public async Task AcBackup004EmptyPlacementAndCrossPartitionDestinationWritesRemainRegisteredAfterDelete()
    {
        using var fixture = new AtomicPartitionRosterFixture();
        fixture.ConfigureGraph();
        var sourceVertex = new EntityRef(AtomicPartitionRosterFixture.Source, AtomicPartitionRosterFixture.Collection, VertexId);
        var destinationVertex = new EntityRef(AtomicPartitionRosterFixture.Destination, AtomicPartitionRosterFixture.Collection, VertexId);
        var sourceSeed = fixture.Batch(AtomicPartitionRosterFixture.Source,
            new PutDocument(AtomicPartitionRosterFixture.Collection, VertexId, DocumentJson));
        fixture.SeedLegacyDocument(AtomicPartitionRosterFixture.Destination,
            AtomicPartitionRosterFixture.Collection, VertexId);
        await Assert.That(sourceSeed.Error).IsNull();
        await Assert.That(fixture.ReadEntry(AtomicPartitionRosterFixture.Destination)).IsNull();
        var edge = fixture.Batch(AtomicPartitionRosterFixture.Source,
            new UpsertEdge(AtomicPartitionRosterFixture.Graph, EdgeId, sourceVertex, destinationVertex, EdgeLabel));
        await Assert.That(edge.Error).IsNull();
        var delivered = fixture.Batch(AtomicPartitionRosterFixture.Destination,
            new ApplyCrossPartitionReverseEdge(AtomicPartitionRosterFixture.Source,
                AtomicPartitionRosterFixture.Graph, EdgeId, destinationVertex, FirstRevision));
        await Assert.That(delivered.Error).IsNull();
        var targetEntry = fixture.ReadEntry(AtomicPartitionRosterFixture.Destination)!;
        var receiver = fixture.Store.Read(view => view.GetRecord<GraphCrossPartitionReceiverStateV1>(
            GraphCrossPartitionKeys.Reverse(AtomicPartitionRosterFixture.Destination,
                AtomicPartitionRosterFixture.Graph, destinationVertex, AtomicPartitionRosterFixture.Source, EdgeId)));
        await Assert.That(receiver).IsNotNull();
        var completed = fixture.Batch(AtomicPartitionRosterFixture.Source,
            new CompleteCrossPartitionReverseEdge(AtomicPartitionRosterFixture.Source,
                AtomicPartitionRosterFixture.Graph, EdgeId, destinationVertex, FirstRevision));
        await Assert.That(completed.Error).IsNull();

        var placementPartition = new PartitionRef(AtomicPartitionRosterFixture.Source.TenantId,
            AtomicPartitionRosterFixture.Source.DatabaseId, AtomicPartitionRosterFixture.Source.TransactionDomainId,
            EmptyPlacementPartitionKey);
        var placement = fixture.Submit(OperationKind.BindAtomicPartitionPlacement,
            new BindAtomicPartitionPlacementRequest(PlacementVersion, InitialPlacementRevision, placementPartition, ShardId));
        var placementEntry = fixture.ReadEntry(placementPartition);
        await Assert.That(placement.Error).IsNull();
        await Assert.That(placementEntry).IsNotNull();
        await Assert.That(fixture.Store.Read(view => AtomicPartitionPlacementSerialization.ReadRow(view, placementPartition)))
            .IsNotNull();

        var sourceEntry = fixture.ReadEntry(AtomicPartitionRosterFixture.Source)!;
        var delete = fixture.Batch(AtomicPartitionRosterFixture.Source,
            new DeleteEdge(AtomicPartitionRosterFixture.Graph, EdgeId, FirstRevision));
        await Assert.That(delete.Error).IsNull();
        await Assert.That(fixture.ReadEntry(AtomicPartitionRosterFixture.Source)).IsEqualTo(sourceEntry);
        await Assert.That(fixture.ReadEntry(AtomicPartitionRosterFixture.Destination)).IsEqualTo(targetEntry);
    }

}
