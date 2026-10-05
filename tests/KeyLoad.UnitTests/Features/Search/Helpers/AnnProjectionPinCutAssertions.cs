using System.Collections.Immutable;
using KeyLoad.Core.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnProjectionPinCutAssertions
{
    internal static async Task AssertPinAsync(TestDatabase database, ProjectionConsumerRef consumer,
        long observedTail, ProjectionConsumerInfo pin)
    {
        await Assert.That(pin.Checkpoint).IsEqualTo(observedTail);
        await Assert.That(pin.Released).IsFalse();
        await Assert.That(database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, consumer.Partition)
            .Consumers.Single().Checkpoint).IsEqualTo(observedTail);
    }

    internal static async Task AssertCapturedAsync(AnnSeed seed, long observedTail, long seedTail,
        ImmutableArray<float> values)
    {
        await Assert.That(seedTail).IsEqualTo(observedTail + 1);
        await Assert.That(seed.Records.Length).IsEqualTo(1);
        var record = seed.Records[0];
        await Assert.That(record.DocumentId).IsEqualTo(AnnProjectionPinTestSupport.VectorId);
        await Assert.That(record.DocumentRevision).IsEqualTo(1L);
        await Assert.That(record.Values.SequenceEqual(values)).IsTrue();
    }

    internal static async Task AssertBridgeAsync(ProjectionBatch bridge, long checkpoint,
        long seedTail, ImmutableArray<float> values)
    {
        await Assert.That(string.IsNullOrWhiteSpace(bridge.Token)).IsFalse();
        await Assert.That(bridge.Consumer.Checkpoint).IsEqualTo(checkpoint);
        await Assert.That(bridge.Entries.Length).IsEqualTo(1);
        var entry = bridge.Entries[0];
        await Assert.That(entry.Sequence).IsEqualTo(seedTail);
        await Assert.That(entry.Mutation).IsTypeOf<PutVector>();
        var vector = (PutVector)entry.Mutation;
        await Assert.That(vector.ExpectedDocumentRevision).IsEqualTo(1L);
        await Assert.That(vector.Values.SequenceEqual(values)).IsTrue();
        await Assert.That(bridge.ThroughSequence).IsEqualTo(seedTail);
        await Assert.That(bridge.HasMore).IsTrue();
    }

    internal static async Task AssertReplayAsync(TestDatabase database,
        ProjectionBatchResult applied, ProjectionBatchResult replayed, long seedTail,
        long afterApplyPosition)
    {
        await Assert.That(applied.Checkpoint).IsEqualTo(seedTail);
        await Assert.That(applied.Receipt.Mutations.Length).IsZero();
        await Assert.That(applied.AlreadyProcessed).IsFalse();
        await Assert.That(NativeSerialization.Serialize(replayed)
            .SequenceEqual(NativeSerialization.Serialize(applied))).IsTrue();
        await Assert.That(replayed.Receipt.Token).IsEqualTo(applied.Receipt.Token);
        await Assert.That(database.Store.Position).IsEqualTo(afterApplyPosition);
    }

    internal static async Task AssertNoSourceEffectsAsync(TestDatabase database,
        ProjectionConsumerRef consumer,
        (OutboxHead Head, long Position, byte[] AtTail, byte[] AfterTail) before,
        long seedTail)
    {
        var status = database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, consumer.Partition);
        await Assert.That(status.Consumers.Single().Checkpoint).IsEqualTo(seedTail);
        await Assert.That(status.Head).IsEqualTo(before.Head);
        await Assert.That(database.Store.Position).IsGreaterThan(before.Position);
        await Assert.That(AnnProjectionPinTestSupport.StoredOutbox(database, seedTail)
            .SequenceEqual(before.AtTail)).IsTrue();
        await Assert.That(AnnProjectionPinTestSupport.StoredOutbox(database, seedTail + 1)
            .SequenceEqual(before.AfterTail)).IsTrue();
    }

    internal static async Task AssertLaterVectorAsync(TestDatabase database,
        ProjectionConsumerRef consumer, long seedTail, ImmutableArray<float> values)
    {
        var later = AnnProjectionPinTestSupport.Read(database, consumer, limit: 1);
        await Assert.That(later.Entries.Length).IsEqualTo(1);
        await Assert.That(later.Entries[0].Sequence).IsEqualTo(seedTail + 1);
        await Assert.That(later.ThroughSequence).IsEqualTo(seedTail + 1);
        await Assert.That(later.HasMore).IsFalse();
        var vector = (PutVector)later.Entries[0].Mutation;
        await Assert.That(vector.ExpectedDocumentRevision).IsEqualTo(1L);
        await Assert.That(vector.Values.SequenceEqual(values)).IsTrue();
        await Assert.That(database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, consumer.Partition)
            .Consumers.Single().Checkpoint).IsEqualTo(seedTail);
    }
}
