using System.Collections.Immutable;
using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class AggregateSnapshotNativeContractTests
{
    private const string TenantId = "tenant";
    private const string DatabaseId = "database";
    private const string DomainId = "orders";
    private const string PartitionKey = "customer-1";

    [Test]
    public async Task AggregateReplayContractsRoundTripThroughGeneratedNativeSerializers()
    {
        var partition = new PartitionRef(TenantId, DatabaseId, DomainId, PartitionKey);
        var stream = new StreamRef(partition, AggregateReplayFixture.StreamSet, AggregateReplayFixture.StreamId, 1);
        var mutation = new StoreAggregateSnapshot(AggregateReplayFixture.StreamSet, AggregateReplayFixture.StreamId,
            1, AggregateReplayFixture.Reducer, 1, AggregateReplayFixture.StateJson, 0, 1);
        var snapshot = AggregateSnapshotPersistence.Create(stream, 1, 1, AggregateReplayFixture.Reducer, 1,
            AggregateReplayFixture.StateJson);
        var request = new ReadAggregateReplayRequest(stream, AggregateReplayFixture.Reducer, 1, false, 2);
        var page = new AggregateReplayPage(stream, new(2, 1, 1), snapshot,
            ImmutableArray<EventRecord>.Empty, 7);

        var restoredMutation = NativeSerialization.Deserialize<StoreAggregateSnapshot>(NativeSerialization.Serialize(mutation));
        var restoredSnapshot = NativeSerialization.Deserialize<AggregateSnapshotState>(NativeSerialization.Serialize(snapshot));
        var restoredRequest = NativeSerialization.Deserialize<ReadAggregateReplayRequest>(NativeSerialization.Serialize(request));
        var restoredPage = NativeSerialization.Deserialize<AggregateReplayPage>(NativeSerialization.Serialize(page));

        await Assert.That(restoredMutation).IsEqualTo(mutation);
        await Assert.That(restoredSnapshot).IsEqualTo(snapshot);
        await Assert.That(restoredRequest).IsEqualTo(request);
        await Assert.That(restoredPage.Stream).IsEqualTo(page.Stream);
        await Assert.That(restoredPage.Head).IsEqualTo(page.Head);
        await Assert.That(restoredPage.Snapshot).IsEqualTo(page.Snapshot);
        await Assert.That(restoredPage.Events.IsEmpty).IsTrue();
        await Assert.That(restoredPage.CutPosition).IsEqualTo(page.CutPosition);
    }
}
