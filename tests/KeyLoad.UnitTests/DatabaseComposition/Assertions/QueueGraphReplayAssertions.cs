using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Messaging;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.DatabaseComposition;

internal static class QueueGraphReplayAssertions
{
    internal static async Task RefusesAsync(QueueGraphReplayState state, DatabaseEngine engine, ZoneTreeStore store)
    {
        foreach (var operation in new[] { state.Source.Healthy, state.Reverse })
        {
            var before = QueueWholeFlowStorage.Bytes(store);
            var position = store.Position;
            var identity = JsonDefaults.Serialize(store.Identity);
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => engine.Apply(operation).Get<CommitReceipt>());
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(failure.Message).IsEqualTo(state.Source.FailureDetail);
            await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(before,
                global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await Assert.That(store.Position).IsEqualTo(position);
            var outcome = engine.ResolveOutcome(operation);
            var expected = Result<object>.Fail(Errors.Problem(ErrorCode.Corruption, state.Source.FailureDetail));
            await Assert.That(OrleansSerialization.Serialize(outcome).AsSpan()
                .SequenceEqual(OrleansSerialization.Serialize(expected))).IsTrue();
            await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(before,
                global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await Assert.That(store.Position).IsEqualTo(position);
            await Assert.That(JsonDefaults.Serialize(store.Identity).AsSpan().SequenceEqual(identity)).IsTrue();
        }
    }

    internal static async Task ReplaysAsync(QueueGraphReplayState state, DatabaseEngine engine, ZoneTreeStore store,
        bool continued)
    {
        var before = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        var originals = new[] { (state.Source.Healthy, state.Source.HealthyReceipt),
            (state.Reverse, state.ReverseReceipt) };
        if (continued)
        {
            originals = [.. originals, (state.Next, state.NextReceipt)];
        }
        foreach (var (operation, bytes) in originals)
        {
            var receipt = engine.Apply(operation).Get<CommitReceipt>();
            var resolved = engine.ResolveOutcome(operation).Get<CommitReceipt>();
            await Assert.That(OrleansSerialization.Serialize(receipt).AsSpan().SequenceEqual(bytes)).IsTrue();
            await Assert.That(OrleansSerialization.Serialize(resolved).AsSpan().SequenceEqual(bytes)).IsTrue();
            await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(before,
                global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await Assert.That(store.Position).IsEqualTo(position);
        }
        var seed = engine.Apply(state.Source.Seed).Get<CommitReceipt>();
        await Assert.That(OrleansSerialization.Serialize(seed).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(state.Source.SeedReceipt))).IsTrue();
        await ModelsAsync(state, engine, store, continued);
        await Assert.That(JsonDefaults.Serialize(store.Identity).AsSpan().SequenceEqual(identity)).IsTrue();
        await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(before,
            global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(store.Position).IsEqualTo(position);
    }

    internal static async Task ContinueAsync(QueueGraphReplayState state, DatabaseEngine engine, ZoneTreeStore store)
    {
        var nextPosition = checked(engine.LastApplied + 1);
        var receipt = engine.Apply(state.Next).Get<CommitReceipt>();
        var expected = new CommitReceipt(state.Next.CommandId, new(store.Identity.Incarnation, state.Source.Fixture.Partition.AtomicPartitionId,
                nextPosition), [new("putDocument", QueueGraphIdentityState.Collection, QueueGraphReplayState.Continuation, 1),
                new("enqueue", QueueGraphIdentityState.Queue, QueueGraphReplayState.NextPrefix +
                    QueueGraphIdentityState.Prefix + QueueGraphIdentityState.Message, 1)], store.Identity.Durability, (store.Identity.Durability == DurabilityProfile.Buffered ? 0 : 1));
        await Assert.That(JsonDefaults.Serialize(receipt).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(receipt.Mutations[0].CompositionReferences).IsEmpty();
        await Assert.That(receipt.Mutations[1].CompositionReferences.SequenceEqual([state.Source.BoundFrom, state.Source.BoundTo])).IsTrue();
        state.NextReceipt = OrleansSerialization.Serialize(receipt);
        await ReplaysAsync(state, engine, store, continued: true);
    }

    internal static async Task ReverseReceiptAsync(QueueGraphIdentityState source, ZoneTreeStore store,
        Guid id, CommitReceipt actual, long nextPosition)
    {
        var expected = new CommitReceipt(id, new(store.Identity.Incarnation, source.Fixture.Partition.AtomicPartitionId,
                nextPosition), [new("enqueue", QueueGraphIdentityState.Queue, QueueGraphReplayState.ReversePrefix +
                QueueGraphIdentityState.Prefix + QueueGraphIdentityState.Message, 1)], store.Identity.Durability, (store.Identity.Durability == DurabilityProfile.Buffered ? 0 : 1));
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(actual.Mutations[0].CompositionReferences.SequenceEqual([source.BoundFrom, source.BoundTo])).IsTrue();
    }

    internal static async Task ModelsAsync(QueueGraphReplayState state, DatabaseEngine engine, ZoneTreeStore store,
        bool continued)
    {
        await QueueGraphIdentityAssertions.ModelsAsync(state.Source, engine, store, healthy: true);
        var payload = QueueGraphIdentityAssertions.Payload(new(state.Source.BoundFrom, state.Source.BoundTo,
            QueueGraphIdentityState.Label, QueueGraphIdentityState.AttributesJson));
        var derived = new MessageInspection(new(QueueGraphReplayState.ReversePrefix +
            QueueGraphIdentityState.Prefix + QueueGraphIdentityState.Message, MessageState.Ready,
            0, 1, 2, null, null), payload, "{}");
        await Assert.That(JsonDefaults.Serialize(engine.InspectMessage(QueueGraphIdentityState.Root,
            new(state.Source.Fixture.Partition, QueueGraphIdentityState.Queue), derived.Metadata.Id))
            .AsSpan().SequenceEqual(JsonDefaults.Serialize(derived))).IsTrue();
        var next = continued ? derived with
        {
            Metadata = new(QueueGraphReplayState.NextPrefix + QueueGraphIdentityState.Prefix +
                QueueGraphIdentityState.Message, MessageState.Ready, 0, 1, 3, null, null)
        } : null;
        await Assert.That(JsonDefaults.Serialize(engine.InspectMessage(QueueGraphIdentityState.Root,
            new(state.Source.Fixture.Partition, QueueGraphIdentityState.Queue),
            QueueGraphReplayState.NextPrefix + QueueGraphIdentityState.Prefix + QueueGraphIdentityState.Message))
            .AsSpan().SequenceEqual(JsonDefaults.Serialize(next))).IsTrue();
        var reference = state.ContinuationReference;
        var expected = continued ? new DocumentRecord(reference, 1, QueueGraphReplayState.ContinuationJson,
            new(), new DateTimeOffset(state.Next.Timestamp)) : null;
        await Assert.That(JsonDefaults.Serialize(store.Read(view => view.GetRecord<DocumentRecord>(
            global::KeyLoad.Core.DocumentStorage.DocumentStorageKeys.RecordKey(reference))))
            .AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
