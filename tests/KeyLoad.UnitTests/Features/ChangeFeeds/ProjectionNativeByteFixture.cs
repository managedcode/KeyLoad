using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

/// <summary>Calibrates quotas from genuine persisted producer and projection entries.</summary>
internal static class ProjectionNativeByteFixture
{
    private const string OutboxSpace = "outbox";
    private const string InputResource = "orders";
    private const string EffectResource = "derived";
    private const string ConsumerName = "projection-v1";
    private const string MutationKind = "putDocument";

    internal static (long Input, long Small, long Large) Calibrate(PutDocument small, PutDocument large)
    {
        var smallBytes = Measure(small);
        var largeBytes = Measure(large);
        if (smallBytes.Input != largeBytes.Input)
        {
            throw new InvalidOperationException("Identical input publications must have identical stored native lengths.");
        }
        return (smallBytes.Input, smallBytes.Effect, largeBytes.Effect);
    }

    internal static int StoredEntryBytes(TestDatabase database, long sequence)
        => database.Store.Read(view => view.ReadOwnedValue(KeySpace.Partition(OutboxSpace, database.Partition, sequence))!.Length);

    private static (long Input, long Effect) Measure(PutDocument effect)
    {
        using var database = new TestDatabase(new() { MaxOutboxBytes = long.MaxValue, ReservedOutboxBytes = 0 });
        database.Configure(InputResource, ResourceKind.Collection);
        database.Configure(EffectResource, ResourceKind.Collection);
        database.Commit(new PutDocument(InputResource, "input", "{}"));
        var consumer = new ProjectionConsumerRef(database.Partition, ConsumerName);
        var configureId = Guid.NewGuid();
        database.Submit(OperationKind.ConfigureProjectionConsumer, new ConfigureProjectionConsumerRequest(configureId, consumer,
            new(1, [InputResource], [MutationKind])), id: configureId).Get<ProjectionConsumerInfo>();
        var batch = database.Database.ReadProjectionBatch("root", new(consumer));
        var completeId = Guid.NewGuid();
        database.Submit(OperationKind.CommitProjectionBatch,
            new CommitProjectionBatchRequest(completeId, consumer, batch.Token, [effect]), id: completeId).Get<ProjectionBatchResult>();
        return (StoredEntryBytes(database, 1), StoredEntryBytes(database, 2));
    }
}
