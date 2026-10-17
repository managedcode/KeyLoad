using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnProjectionPinRetentionAssertions
{
    internal static async Task AssertPurgeBlockedAsync(TestDatabase database,
        ProjectionConsumerRef consumer, long checkpoint, long sourceTail, OutboxStatus before,
        ImmutableArray<byte[]> retained)
    {
        var blocked = AnnProjectionPinTestSupport.Purge(database, sourceTail);
        await Assert.That(blocked.Error).IsEqualTo(ErrorCode.Conflict);
        var unchanged = database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, consumer.Partition);
        await Assert.That(unchanged.Head).IsEqualTo(before.Head);
        await Assert.That(unchanged.Consumers.Single().Checkpoint).IsEqualTo(checkpoint);
        for (var sequence = 1; sequence <= sourceTail; sequence++)
        {
            var expected = retained[checked((int)sequence - 1)];
            var actual = AnnProjectionPinTestSupport.StoredOutbox(database, sequence);
            await Assert.That(actual.SequenceEqual(expected)).IsTrue();
        }
    }

    internal static async Task AssertBridgeAsync(ProjectionBatch bridge, long seedTail,
        ImmutableArray<float> capturedValues)
    {
        await Assert.That(string.IsNullOrWhiteSpace(bridge.Token)).IsFalse();
        await Assert.That(bridge.Entries.Length).IsEqualTo(1);
        var entry = bridge.Entries[0];
        await Assert.That(entry.Sequence).IsEqualTo(seedTail);
        await Assert.That(entry.Mutation).IsTypeOf<PutVector>();
        var vector = (PutVector)entry.Mutation;
        await Assert.That(vector.ExpectedDocumentRevision).IsEqualTo(1L);
        await Assert.That(vector.Values.SequenceEqual(capturedValues)).IsTrue();
        await Assert.That(bridge.ThroughSequence).IsEqualTo(seedTail);
        await Assert.That(bridge.HasMore).IsTrue();
    }

    internal static async Task AssertPurgedThroughSeedAsync(OutboxHead purged,
        long seedTail, long sourceTail)
    {
        await Assert.That(purged.FirstAvailable).IsEqualTo(seedTail + 1);
        await Assert.That(purged.Tail).IsEqualTo(sourceTail);
        await Assert.That(purged.StoredRecords).IsEqualTo(1L);
    }

    internal static async Task AssertLaterEntryAsync(ProjectionBatch later,
        long sourceTail, ImmutableArray<float> values)
    {
        await Assert.That(later.Entries.Length).IsEqualTo(1);
        await Assert.That(later.Entries[0].Sequence).IsEqualTo(sourceTail);
        var vector = (PutVector)later.Entries[0].Mutation;
        await Assert.That(vector.ExpectedDocumentRevision).IsEqualTo(1L);
        await Assert.That(vector.Values.SequenceEqual(values)).IsTrue();
        await Assert.That(later.ThroughSequence).IsEqualTo(sourceTail);
    }

    internal static async Task AssertReleasedTokenAsync(TestDatabase database,
        ProjectionConsumerRef consumer, ProjectionBatch batch, long checkpoint)
    {
        var failure = AnnProjectionPinTestSupport.CommitEmpty(database, consumer, batch,
            AnnProjectionPinTestSupport.Principal, Guid.NewGuid(), TimeProvider.System.GetUtcNow());
        await Assert.That(failure.Error).IsEqualTo(ErrorCode.TokenInvalidated);
        var status = database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, consumer.Partition);
        await Assert.That(status.Consumers.Single().Released).IsTrue();
        await Assert.That(status.Consumers.Single().Checkpoint).IsEqualTo(checkpoint);
    }

    internal static async Task AssertReleasedIdentityCannotBeReusedAsync(
        TestDatabase database, ProjectionConsumerRef consumer, long sourceTail)
    {
        var commandId = Guid.NewGuid();
        var changed = database.Submit(OperationKind.ConfigureProjectionConsumer,
            new ConfigureProjectionConsumerRequest(commandId, consumer,
                new(2, [AnnProjectionPinTestSupport.Collection], [AnnProjectionPinTestSupport.VectorMutation]), sourceTail),
            id: commandId);
        await Assert.That(changed.Error).IsEqualTo(ErrorCode.Conflict);
        var state = database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, consumer.Partition).Consumers.Single();
        await Assert.That(state.Released).IsTrue();
    }

    internal static async Task AssertReclaimedAsync(TestDatabase database,
        ProjectionConsumerRef consumer, OutboxHead reclaimed, long sourceTail)
    {
        await Assert.That(reclaimed.FirstAvailable).IsEqualTo(sourceTail + 1);
        await Assert.That(reclaimed.Tail).IsEqualTo(sourceTail);
        await Assert.That(reclaimed.StoredRecords).IsZero();
        await Assert.That(database.Database.GetOutboxStatus(AnnProjectionPinTestSupport.Principal, consumer.Partition)
            .Consumers.Single().Released).IsTrue();
    }
}
