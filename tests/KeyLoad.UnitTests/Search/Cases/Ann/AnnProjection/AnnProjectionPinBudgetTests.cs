using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Search;

internal sealed class AnnProjectionPinBudgetTests
{
    private const string ByteBoundaryConsumer = "ann-pin-byte-boundary";
    private const string CorruptHistoryConsumer = "ann-pin-corrupt-history";
    private static readonly ImmutableArray<float> FirstValues = [1f, 2f, 3f];

    [Test]
    public async Task ProjectionBatchByteBoundaryUsesTheActualOutboxRecordLength()
    {
        var runtimeBytesCore = MeasureVectorEntryBytes();
        using var database = AnnProjectionPinTestSupport.Create(new()
        {
        });
        var consumer = AnnProjectionPinTestSupport.Consumer(database, ByteBoundaryConsumer);
        var start = AnnProjectionPinTestSupport.Tail(database);
        AnnProjectionPinTestSupport.Configure(database, consumer, start);
        AnnProjectionPinTestSupport.PutVector(database, FirstValues);
        var stored = AnnProjectionPinTestSupport.StoredOutbox(database, start + 1);
        await Assert.That(stored.Length).IsEqualTo(runtimeBytesCore);
        var beforeHead = database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, database.Partition).Head;
        var beforeConsumer = AnnProjectionPinTestSupport.StoredConsumer(database, consumer);

        var oneByteShort = Assert.ThrowsExactly<KeyLoadException>(() =>
            AnnProjectionPinTestSupport.Read(database, consumer, limit: 1, maxBytes: runtimeBytesCore - 1));
        await Assert.That(oneByteShort.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, database.Partition).Consumers
            .Single().Checkpoint).IsEqualTo(start);
        await Assert.That(database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, database.Partition).Head)
            .IsEqualTo(beforeHead);
        await Assert.That(AnnProjectionPinTestSupport.StoredConsumer(database, consumer)
            .SequenceEqual(beforeConsumer)).IsTrue();
        await Assert.That(AnnProjectionPinTestSupport.StoredOutbox(database, start + 1)
            .SequenceEqual(stored)).IsTrue();

        var exact = AnnProjectionPinTestSupport.Read(database, consumer, limit: 1, maxBytes: runtimeBytesCore);
        await Assert.That(exact.Entries.Length).IsEqualTo(1);
        await Assert.That(exact.ThroughSequence).IsEqualTo(start + 1);
        await Assert.That(exact.Entries[0].Sequence).IsEqualTo(start + 1);
    }

    [Test]
    public async Task UnavailableStartAndMalformedOrMissingHistoryFailWithoutCheckpointAdvance()
    {
        using var database = AnnProjectionPinTestSupport.Create();
        var originalHead = database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, database.Partition).Head;
        var purge = AnnProjectionPinTestSupport.Purge(database, 1).Get<OutboxHead>();
        await Assert.That(purge.FirstAvailable).IsEqualTo(2L);
        var consumer = AnnProjectionPinTestSupport.Consumer(database, CorruptHistoryConsumer);
        var checkpoint = purge.FirstAvailable - 1;
        AnnProjectionPinTestSupport.Configure(database, consumer, checkpoint);
        await AnnProjectionPinHistoryAssertions.AssertUnavailableStartAsync(database, purge);
        var entry = AnnProjectionPinHistoryAssertions.CorruptSequence(database, consumer,
            purge.FirstAvailable);
        await AnnProjectionPinHistoryAssertions.AssertCorruptReadUnchangedAsync(database,
            consumer, checkpoint, purge.FirstAvailable, entry);
        AnnProjectionPinHistoryAssertions.Restore(database, purge.FirstAvailable, entry.Original);
        await AnnProjectionPinHistoryAssertions.AssertGapReadUnchangedAsync(database,
            consumer, checkpoint, purge.FirstAvailable, entry.ConsumerBytes);
        AnnProjectionPinHistoryAssertions.Restore(database, purge.FirstAvailable, entry.Original);
        await Assert.That(database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, database.Partition).Head.Tail)
            .IsEqualTo(originalHead.Tail);
    }

    private static int MeasureVectorEntryBytes()
    {
        using var database = AnnProjectionPinTestSupport.Create();
        var start = AnnProjectionPinTestSupport.Tail(database);
        AnnProjectionPinTestSupport.PutVector(database, FirstValues);
        return AnnProjectionPinTestSupport.StoredOutbox(database, start + 1).Length;
    }
}
