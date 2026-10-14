using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

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
            var expected = new OperationResult(null, ErrorCode.Corruption, state.Source.FailureDetail);
            await Assert.That(NativeSerialization.Serialize(outcome).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
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
            await Assert.That(NativeSerialization.Serialize(receipt).AsSpan().SequenceEqual(bytes)).IsTrue();
            await Assert.That(NativeSerialization.Serialize(resolved).AsSpan().SequenceEqual(bytes)).IsTrue();
            await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(before,
                global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await Assert.That(store.Position).IsEqualTo(position);
        }
        var seed = engine.Apply(state.Source.Seed).Get<CommitReceipt>();
        await Assert.That(NativeSerialization.Serialize(seed).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(state.Source.SeedReceipt))).IsTrue();
        await ModelsAsync(state, engine, store, continued);
        await Assert.That(JsonDefaults.Serialize(store.Identity).AsSpan().SequenceEqual(identity)).IsTrue();
        await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(before,
            global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(store.Position).IsEqualTo(position);
    }

    internal static async Task ContinueAsync(QueueGraphReplayState state, DatabaseEngine engine, ZoneTreeStore store)
    {
        var receipt = engine.Apply(state.Next).Get<CommitReceipt>();
        var expected = new CommitReceipt(state.Next.Id,
            new(store.Identity.Incarnation, state.Source.Fixture.Partition.AtomicPartitionId,
                store.Position, state.Source.SeedReceipt.Token.OwnershipEpoch),
            [new("putDocument", QueueGraphIdentityState.Collection, QueueGraphReplayState.Continuation, 1),
                new("enqueue", QueueGraphIdentityState.Queue, QueueGraphReplayState.NextPrefix +
                    QueueGraphIdentityState.Prefix + QueueGraphIdentityState.Message, 1)],
            state.Source.SeedReceipt.Durability);
        await Assert.That(JsonDefaults.Serialize(receipt).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(receipt.Mutations[0].CompositionReferences).IsEmpty();
        await Assert.That(receipt.Mutations[1].CompositionReferences.SequenceEqual([state.Source.From, state.Source.To])).IsTrue();
        state.NextReceipt = NativeSerialization.Serialize(receipt);
        await ReplaysAsync(state, engine, store, continued: true);
    }

    internal static async Task ReverseReceiptAsync(QueueGraphIdentityState source, ZoneTreeStore store,
        Guid id, CommitReceipt actual)
    {
        var expected = new CommitReceipt(id,
            new(store.Identity.Incarnation, source.Fixture.Partition.AtomicPartitionId,
                store.Position, source.SeedReceipt.Token.OwnershipEpoch),
            [new("enqueue", QueueGraphIdentityState.Queue, QueueGraphReplayState.ReversePrefix +
                QueueGraphIdentityState.Prefix + QueueGraphIdentityState.Message, 1)], source.SeedReceipt.Durability);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(actual.Mutations[0].CompositionReferences.SequenceEqual([source.From, source.To])).IsTrue();
    }

    internal static async Task ModelsAsync(QueueGraphReplayState state, DatabaseEngine engine, ZoneTreeStore store,
        bool continued)
    {
        await QueueGraphIdentityAssertions.ModelsAsync(state.Source, engine, store, healthy: true);
        var original = state.Source.Source;
        var derived = new MessageInspection(new(QueueGraphReplayState.ReversePrefix +
            QueueGraphIdentityState.Prefix + QueueGraphIdentityState.Message, MessageState.Ready,
            0, 1, 2, null, null), original.PayloadJson, "{}");
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
        var reference = new EntityRef(state.Source.Fixture.Partition, QueueGraphIdentityState.Collection,
            QueueGraphReplayState.Continuation);
        var expected = continued ? new DocumentRecord(reference, 1, QueueGraphReplayState.ContinuationJson,
            new(), state.Next.EvaluatedAt) : null;
        await Assert.That(JsonDefaults.Serialize(store.Read(view => view.GetRecord<DocumentRecord>(
            global::KeyLoad.Core.Features.DocumentStorage.DocumentStorageKeys.RecordKey(reference))))
            .AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
