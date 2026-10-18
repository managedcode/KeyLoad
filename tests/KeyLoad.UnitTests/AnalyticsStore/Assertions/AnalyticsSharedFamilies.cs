using KeyLoad.Core.Features.AnalyticsStore;
using KeyLoad.Core.Features.BackupRestore.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Queries;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;
using P = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsSharedProtocol;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal static class AnalyticsSharedFamilies
{
    internal static async Task RunAsync(CancellationToken token)
    {
        using var fixture = new TestDatabase();
        AnalyticsSharedFlow.Configure(fixture);
        var partition = fixture.Partition with
        {
            PartitionKey = P.AnalyticsPartition
        };
        var upsert = P.Upsert([]);
        var command = new CommandRequest(Guid.NewGuid(), partition, [upsert]);
        var receipt = await AnalyticsSharedFlow.CommitAsync(fixture, command,
            [new(AnalyticsStoreProtocol.UpsertKind, P.Store, P.EventId, 1)]);
        var roster = fixture.Store.Read(view => view.GetRecord<AtomicPartitionCatalogEntryV1>(AtomicPartitionRosterKeys.Partition(partition)));
        await AnalyticsSharedAssertions.SameAsync(new AtomicPartitionCatalogEntryV1(1, partition, 0, receipt.Token.Position), roster);
        var lifetime = fixture.Store.Read(view => ResourceLifetimeCatalog.Read(view, partition.TenantId, partition.DatabaseId,
            P.Store, fixture.Database.Resource(view, partition, P.Store)))!;
        await Assert.That(lifetime.ResourceName).IsEqualTo(P.Store);
        await Assert.That(lifetime.Kind).IsEqualTo(ResourceKind.AnalyticsStore);
        await Assert.That(lifetime.Incarnation.Value).IsNotEmpty();
        var evaluatedAt = new DateTimeOffset(fixture.ReadStoredOutcome(command.CommandId).PreparedCommand!.Timestamp);
        var row = new AnalyticsStoredEvent(new(P.EventId, P.Epoch, P.Dimensions, P.Metrics, null, [], 1), false, evaluatedAt);
        await PageAsync(fixture, partition, PartitionRecordFamilies.AnalyticsEvent,
            AnalyticsStoreKeys.Event(partition, P.Store, P.EventId), NativeSerialization.Serialize(row), token);
        await PageAsync(fixture, partition, PartitionRecordFamilies.AnalyticsGeneration,
            AnalyticsStoreKeys.Generation(partition, P.Store), NativeSerialization.Serialize(1L), token);
        await Assert.That(PartitionRecordFamilies.All.SequenceEqual(PartitionRecordFamilies.All.Order(StringComparer.Ordinal))).IsTrue();
        await ProjectionAsync(fixture, partition, upsert, receipt, evaluatedAt);
    }

    private static async Task ProjectionAsync(TestDatabase fixture, PartitionRef partition,
        UpsertAnalyticsEvents upsert, CommitReceipt receipt, DateTimeOffset evaluatedAt)
    {
        var consumer = new ProjectionConsumerRef(partition, P.Projection);
        var id = Guid.NewGuid();
        var definition = new ProjectionConsumerDefinition(1, [P.Store],
            [AnalyticsStoreProtocol.UpsertKind, AnalyticsStoreProtocol.DeleteKind]);
        var configured = fixture.Submit(OperationKind.ConfigureProjectionConsumer,
            new ConfigureProjectionConsumerRequest(id, consumer, definition), id: id).Get<ProjectionConsumerInfo>();
        await AnalyticsSharedAssertions.SameAsync(definition, configured.Definition);
        var batch = fixture.Database.ReadProjectionBatch(P.Root, new(consumer));
        await Assert.That(batch.Entries.Length).IsEqualTo(1);
        await AnalyticsStoreNativeTestOperations.EqualAsync(new OutboxEntry(1, 0, receipt.Token, evaluatedAt, upsert,
            new(AnalyticsStoreProtocol.UpsertKind, P.Store, P.EventId, 1)), batch.Entries.Single());
        await Assert.That(batch.Entries.Single().Receipt.CompositionReferences).IsEmpty();
    }

    private static async Task PageAsync(TestDatabase fixture, PartitionRef partition, string family,
        byte[] key, byte[] value, CancellationToken token)
    {
        var page = fixture.Store.Read(view => PartitionRecordPageReader.Read(view, partition, family,
            10, new DatabaseLimits().MaxBatchBytes, new DatabaseLimits().MaxQueryReadBytes, cancellationToken: token));
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Continuation).IsNull();
        await Assert.That(page.Records.Length).IsEqualTo(1);
        await Assert.That(Convert.ToHexString(page.Records.Single().Key.Span)).IsEqualTo(Convert.ToHexString(key));
        await Assert.That(Convert.ToHexString(page.Records.Single().Value.Span)).IsEqualTo(Convert.ToHexString(value));
        await Assert.That(page.ExaminedBytes).IsGreaterThan(0L);
    }
}
