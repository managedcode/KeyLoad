using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnProjectionPinBudgetTests
{
    private const string FirstConsumer = "ann-pin-budget-first";
    private const string SecondConsumer = "ann-pin-budget-second";
    private const string ExcessConsumer = "ann-pin-budget-excess";
    private const string ByteBoundaryConsumer = "ann-pin-byte-boundary";
    private const string CorruptHistoryConsumer = "ann-pin-corrupt-history";
    private static readonly ImmutableArray<float> FirstValues = [1f, 2f, 3f];
    private static readonly ImmutableArray<float> SecondValues = [4f, 5f, 6f];
    private static readonly ImmutableArray<float> ThirdValues = [7f, 8f, 9f];

    [Test]
    public async Task ConsumerAndNativeBatchRecordCountsEnforceInclusiveLimits()
    {
        using var database = AnnProjectionPinTestSupport.Create(new()
        {
            MaxResults = 2,
            MaxProjectionConsumers = 2
        });
        var start = AnnProjectionPinTestSupport.Tail(database);
        var first = AnnProjectionPinTestSupport.Consumer(database, FirstConsumer);
        var second = AnnProjectionPinTestSupport.Consumer(database, SecondConsumer);
        AnnProjectionPinTestSupport.Configure(database, first, start);
        AnnProjectionPinTestSupport.Configure(database, second, start);

        var excess = AnnProjectionPinTestSupport.Consumer(database, ExcessConsumer);
        var excessId = Guid.NewGuid();
        var rejectedConsumer = database.Submit(OperationKind.ConfigureProjectionConsumer,
            new ConfigureProjectionConsumerRequest(excessId, excess,
                new(1, [AnnProjectionPinTestSupport.Collection], [AnnProjectionPinTestSupport.VectorMutation]), start), id: excessId);
        await Assert.That(rejectedConsumer.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, database.Partition).Consumers.Length).IsEqualTo(2);

        AnnProjectionPinTestSupport.PutVector(database, FirstValues);
        AnnProjectionPinTestSupport.PutVector(database, SecondValues);
        AnnProjectionPinTestSupport.PutVector(database, ThirdValues);
        var headBefore = database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, database.Partition).Head;
        var checkpointBefore = database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, database.Partition)
            .Consumers.Single(info => info.Consumer == first).Checkpoint;
        var exact = AnnProjectionPinTestSupport.Read(database, first, limit: 2);
        await Assert.That(exact.Entries.Length).IsEqualTo(2);
        await Assert.That(exact.ThroughSequence).IsEqualTo(start + 2);
        await Assert.That(exact.HasMore).IsTrue();
        await Assert.That(exact.Entries.Select(entry => entry.Sequence))
            .IsEquivalentTo(new long[] { start + 1, start + 2 });

        var excessLimit = Assert.ThrowsExactly<KeyLoadException>(() =>
            AnnProjectionPinTestSupport.Read(database, first, limit: 3));
        await Assert.That(excessLimit.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        var after = database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, database.Partition);
        await Assert.That(after.Head).IsEqualTo(headBefore);
        await Assert.That(after.Consumers.Single(info => info.Consumer == first).Checkpoint)
            .IsEqualTo(checkpointBefore);
    }

    [Test]
    public async Task ProjectionBatchByteBoundaryUsesTheActualNativeOutboxRecordLength()
    {
        var nativeBytes = MeasureVectorEntryBytes();
        using var database = AnnProjectionPinTestSupport.Create(new() { MaxProjectionBatchBytes = nativeBytes });
        var consumer = AnnProjectionPinTestSupport.Consumer(database, ByteBoundaryConsumer);
        var start = AnnProjectionPinTestSupport.Tail(database);
        AnnProjectionPinTestSupport.Configure(database, consumer, start);
        AnnProjectionPinTestSupport.PutVector(database, FirstValues);
        var stored = AnnProjectionPinTestSupport.StoredOutbox(database, start + 1);
        await Assert.That(stored.Length).IsEqualTo(nativeBytes);
        var beforeHead = database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, database.Partition).Head;
        var beforeConsumer = AnnProjectionPinTestSupport.StoredConsumer(database, consumer);

        var oneByteShort = Assert.ThrowsExactly<KeyLoadException>(() =>
            AnnProjectionPinTestSupport.Read(database, consumer, limit: 1, maxBytes: nativeBytes - 1));
        await Assert.That(oneByteShort.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, database.Partition).Consumers
            .Single().Checkpoint).IsEqualTo(start);
        await Assert.That(database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, database.Partition).Head)
            .IsEqualTo(beforeHead);
        await Assert.That(AnnProjectionPinTestSupport.StoredConsumer(database, consumer)
            .SequenceEqual(beforeConsumer)).IsTrue();
        await Assert.That(AnnProjectionPinTestSupport.StoredOutbox(database, start + 1)
            .SequenceEqual(stored)).IsTrue();

        var exact = AnnProjectionPinTestSupport.Read(database, consumer, limit: 1, maxBytes: nativeBytes);
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
