using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class ProjectionPartitionIsolationFixture
{
    internal const string Sibling = "customer-2";
    internal const string ScopeDetail = "The projection batch belongs to a different generation or scope.";
    internal const string PinDetail = "A projection consumer or rebuild pins the requested outbox history.";
    internal const string PutKind = "putDocument";
    internal const string HealthySource = "c";
    internal const string HealthyEffect = "healthy";
    internal const string HealthyJson = "{\"n\":3}";
    private const string HeadSpace = "outbox-head";
    internal const string EntrySpace = "outbox";

    internal static async Task<ProjectionConsumerIdentityState> SiblingAsync(TestDatabase database,
        ProjectionConsumerIdentityState first, CancellationToken token)
    {
        var partition = first.Partition with
        {
            PartitionKey = Sibling
        };
        var consumer = first.Consumer with
        {
            Partition = partition
        };
        var id = Guid.NewGuid();
        var configure = ProjectionConsumerIdentityFixture.Operation(OperationKind.ConfigureProjectionConsumer, id,
            new ConfigureProjectionConsumerRequest(id, consumer, first.Definition));
        var configured = database.Database.ApplyEmbedded(configure, token);
        await ProjectionConsumerIdentityAssertions.ExactAsync(configured.Get<ProjectionConsumerInfo>(),
            new ProjectionConsumerInfo(consumer, first.Definition, 0L, false));
        var seedId = Guid.NewGuid();
        var seed = ProjectionConsumerIdentityFixture.Operation(OperationKind.Batch, seedId,
            new CommandRequest(seedId, partition, [new PutDocument(ProjectionConsumerIdentityFixture.Source, "a", "{\"n\":1}", 0L),
                new PutDocument(ProjectionConsumerIdentityFixture.Source, "b", "{\"n\":2}", 0L)])) with
        {
            EvaluatedAt = first.Time
        };
        token.ThrowIfCancellationRequested();
        var seeded = database.Database.Apply(seed);
        var receipt = seeded.Get<CommitReceipt>();
        await ProjectionConsumerIdentityAssertions.ReceiptAsync(seed, receipt,
            new(PutKind, ProjectionConsumerIdentityFixture.Source, "a", 1L),
            new(PutKind, ProjectionConsumerIdentityFixture.Source, "b", 1L));
        var batch = database.Database.ReadProjectionBatch(ProjectionConsumerIdentityFixture.Root, new(consumer));
        var completeId = Guid.NewGuid();
        var complete = ProjectionConsumerIdentityFixture.Operation(OperationKind.CommitProjectionBatch, completeId,
            new CommitProjectionBatchRequest(completeId, consumer, batch.Token,
                [new PutDocument(ProjectionConsumerIdentityFixture.Target, "effect", "{\"sourceCount\":2}", 0L)]));
        var againId = Guid.NewGuid();
        var again = ProjectionConsumerIdentityFixture.Operation(OperationKind.ConfigureProjectionConsumer, againId,
            new ConfigureProjectionConsumerRequest(againId, consumer, first.Definition));
        var key = KeySpace.Partition(ProjectionConsumerIdentityFixture.ConsumerSpace, partition, consumer.Name);
        var original = database.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException(ProjectionConsumerIdentityFixture.MissingRow);
        return new(partition, consumer, first.Definition, configure, configured, seed, seeded, receipt,
            first.Time, batch, complete, again, key, original);
    }

    internal static byte[][] OriginalKeys(ProjectionConsumerIdentityState source) =>
    [
        source.Key, KeySpace.Partition(HeadSpace, source.Partition),
        KeySpace.Partition(EntrySpace, source.Partition, 1L), KeySpace.Partition(EntrySpace, source.Partition, 2L),
        DocumentStorageKeys.RecordKey(new EntityRef(source.Partition, ProjectionConsumerIdentityFixture.Source, "a")),
        DocumentStorageKeys.RecordKey(new EntityRef(source.Partition, ProjectionConsumerIdentityFixture.Source, "b"))
    ];

    internal static string[] SelectedBytes(ZoneTreeStore store, byte[][] keys)
        => store.Read(view => keys.Select(key => Convert.ToHexString(key) + ":" +
            Convert.ToHexString(view.ReadOwnedValue(key) ?? throw new InvalidOperationException(ProjectionConsumerIdentityFixture.MissingRow))).ToArray());

    internal static Command<DatabaseCommand> Purge(PartitionRef partition, long through)
    {
        var id = Guid.NewGuid();
        return ProjectionConsumerIdentityFixture.Operation(OperationKind.PurgeOutbox, id, new PurgeOutboxRequest(id, partition, through));
    }
}
