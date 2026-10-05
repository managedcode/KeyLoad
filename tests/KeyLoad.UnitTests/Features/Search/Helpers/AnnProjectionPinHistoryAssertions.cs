using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnProjectionPinHistoryAssertions
{
    private const string UnavailableConsumerName = "ann-pin-history-unavailable";
    private const string OutboxSpace = "outbox";

    internal static async Task AssertUnavailableStartAsync(TestDatabase database, OutboxHead before)
    {
        var consumer = AnnProjectionPinTestSupport.Consumer(database, UnavailableConsumerName);
        var commandId = Guid.NewGuid();
        var result = database.Submit(OperationKind.ConfigureProjectionConsumer,
            new ConfigureProjectionConsumerRequest(commandId, consumer,
                new(1, [AnnProjectionPinTestSupport.Collection], [AnnProjectionPinTestSupport.VectorMutation]), 0), id: commandId);
        await Assert.That(result.Error).IsEqualTo(ErrorCode.HistoryUnavailable);
        await Assert.That(database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, database.Partition).Head)
            .IsEqualTo(before);
        await Assert.That(AnnProjectionPinTestSupport.ConsumerExists(database, consumer)).IsFalse();
    }

    internal static (byte[] Original, byte[] Malformed, byte[] ConsumerBytes)
        CorruptSequence(TestDatabase database, ProjectionConsumerRef consumer, long sequence)
    {
        var key = KeySpace.Partition(OutboxSpace, database.Partition, sequence);
        var original = database.Store.Read(view => view.ReadOwnedValue(key)!);
        var entry = NativeSerialization.Deserialize<OutboxEntry>(original) with
        { Sequence = sequence + 1 };
        var malformed = NativeSerialization.Serialize(entry);
        var consumerBytes = AnnProjectionPinTestSupport.StoredConsumer(database, consumer);
        database.Store.Commit((transaction, _) =>
        {
            transaction.Put(key, malformed);
            return 0;
        });
        return (original, malformed, consumerBytes);
    }

    internal static async Task AssertCorruptReadUnchangedAsync(TestDatabase database,
        ProjectionConsumerRef consumer, long checkpoint, long sequence,
        (byte[] Original, byte[] Malformed, byte[] ConsumerBytes) entry)
    {
        var damagedHead = database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, database.Partition).Head;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            AnnProjectionPinTestSupport.Read(database, consumer));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, database.Partition).Head)
            .IsEqualTo(damagedHead);
        await Assert.That(CurrentCheckpoint(database, consumer)).IsEqualTo(checkpoint);
        await Assert.That(AnnProjectionPinTestSupport.StoredConsumer(database, consumer)
            .SequenceEqual(entry.ConsumerBytes)).IsTrue();
        await Assert.That(AnnProjectionPinTestSupport.StoredOutbox(database, sequence)
            .SequenceEqual(entry.Malformed)).IsTrue();
    }

    internal static async Task AssertGapReadUnchangedAsync(TestDatabase database,
        ProjectionConsumerRef consumer, long checkpoint, long sequence, byte[] consumerBytes)
    {
        DeleteEntry(database, sequence);
        var before = database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, database.Partition).Head;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            AnnProjectionPinTestSupport.Read(database, consumer));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, database.Partition).Head)
            .IsEqualTo(before);
        await Assert.That(CurrentCheckpoint(database, consumer)).IsEqualTo(checkpoint);
        await Assert.That(AnnProjectionPinTestSupport.StoredConsumer(database, consumer)
            .SequenceEqual(consumerBytes)).IsTrue();
    }

    internal static void Restore(TestDatabase database, long sequence, byte[] original)
    {
        var key = KeySpace.Partition(OutboxSpace, database.Partition, sequence);
        database.Store.Commit((transaction, _) =>
        {
            transaction.Put(key, original);
            return 0;
        });
    }

    private static long CurrentCheckpoint(TestDatabase database, ProjectionConsumerRef consumer)
        => database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, consumer.Partition)
            .Consumers.Single().Checkpoint;

    private static void DeleteEntry(TestDatabase database, long sequence)
    {
        var key = KeySpace.Partition(OutboxSpace, database.Partition, sequence);
        database.Store.Commit((transaction, _) =>
        {
            transaction.Delete(key);
            return 0;
        });
    }
}
