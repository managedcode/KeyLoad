using System.Text;
using KeyLoad.Core;
using KeyLoad.CrashHost.Features.Search;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Search;

internal static class EventProjectionRecoveryStoreOracle
{
    internal static ProjectionCrashCut CaptureCut(ZoneTreeStore store, ReplicatedOperation operation,
        ApplyVectorProjection projection, long priorOutboxTail)
        => store.Read(view => new ProjectionCrashCut(
            view.ReadOwnedValue(EventProjectionCrashScenario.VectorKey(EventProjectionCrashScenario.TargetId)),
            view.ReadOwnedValue(EventProjectionCrashScenario.LineageKey()),
            view.ReadOwnedValue(EventProjectionCrashScenario.EffectKey(projection)),
            view.ReadOwnedValue(OutcomeStoreOracle.Key(store, operation)),
            view.ReadOwnedValue(EventProjectionCrashScenario.OutboxHeadKey()),
            view.ReadOwnedValue(EventProjectionCrashScenario.OutboxEntryKey(priorOutboxTail + 1))));

    internal static async Task<(OutboxHead Head, byte[] Bytes)> ReadBeforeOutboxHeadAsync(string root,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(root, EventProjectionCrashScenario.OutboxHeadFile),
            cancellationToken);
        return (NativeSerialization.Deserialize<OutboxHead>(bytes), bytes);
    }

    internal static async Task AssertCutAsync(ProjectionCrashCut cut, bool committed,
        OutboxHead beforeHead, byte[] beforeHeadBytes, long beforePosition, long currentPosition,
        CommitStage stage)
    {
        await Assert.That(cut.Vector is not null).IsEqualTo(committed);
        await Assert.That(cut.Lineage is not null).IsEqualTo(committed);
        await Assert.That(cut.Effect is not null).IsEqualTo(committed);
        await Assert.That(cut.Outcome is not null).IsEqualTo(committed);
        await Assert.That(cut.OutboxEntry is not null).IsEqualTo(committed);
        var currentHeadBytes = cut.OutboxHead
            ?? throw new InvalidOperationException("The outbox head was missing after store reopen.");
        var currentHead = NativeSerialization.Deserialize<OutboxHead>(currentHeadBytes);
        await Assert.That(currentHead.Tail).IsEqualTo(committed ? beforeHead.Tail + 1 : beforeHead.Tail);
        await Assert.That(currentHead.FirstAvailable).IsEqualTo(beforeHead.FirstAvailable);
        await Assert.That(currentHead.StoredRecords).IsEqualTo(beforeHead.StoredRecords + (committed ? 1 : 0));
        var expectedStoredBytes = committed
            ? beforeHead.StoredBytes + cut.OutboxEntry!.Length
            : beforeHead.StoredBytes;
        await Assert.That(currentHead.StoredBytes).IsEqualTo(expectedStoredBytes);
        if (!committed)
        {
            await Assert.That(currentHeadBytes.AsSpan().SequenceEqual(beforeHeadBytes)).IsTrue();
        }
        await Assert.That(currentPosition).IsEqualTo(beforePosition + (committed ? 1 : 0));
        if (committed)
        {
            await AssertNoCopiedPayloadAsync(cut);
        }
        if (stage >= CommitStage.JournalFlushed)
        {
            await Assert.That(committed).IsTrue();
        }
    }

    internal static async Task AssertSeedBytesAsync(string root, ZoneTreeStore store, bool includeDocuments,
        CancellationToken cancellationToken)
    {
        if (includeDocuments)
        {
            await AssertStoredBytesAsync(root, store, EventProjectionCrashScenario.SourceDocumentFile,
                EventProjectionCrashScenario.DocumentKey(EventProjectionCrashScenario.SourceId), cancellationToken);
            await AssertStoredBytesAsync(root, store, EventProjectionCrashScenario.TargetDocumentFile,
                EventProjectionCrashScenario.DocumentKey(EventProjectionCrashScenario.TargetId), cancellationToken);
        }
        await AssertStoredBytesAsync(root, store, EventProjectionCrashScenario.StreamHeadFile,
            EventProjectionCrashScenario.StreamHeadKey(), cancellationToken);
        await AssertStoredBytesAsync(root, store, EventProjectionCrashScenario.EventRecordFile,
            EventProjectionCrashScenario.EventKey(), cancellationToken);
        await AssertStoredBytesAsync(root, store, EventProjectionCrashScenario.EventIdentityFile,
            EventProjectionCrashScenario.EventIdentityKey(), cancellationToken);
    }

    internal static async Task AssertReceiptAndOutboxAsync(ZoneTreeStore store, DatabaseEngine database,
        ReplicatedOperation operation, CommitReceipt receipt, long beforeTail)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(operation.Id);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(1);
        await Assert.That(receipt.Mutations[0].Kind).IsEqualTo(EventProjectionCrashScenario.MutationKind);
        await Assert.That(receipt.Mutations[0].Resource).IsEqualTo(EventProjectionCrashScenario.Collection);
        await Assert.That(receipt.Mutations[0].Id).IsEqualTo(EventProjectionCrashScenario.TargetId);
        await Assert.That(receipt.Mutations[0].Revision).IsEqualTo(EventProjectionCrashScenario.DocumentRevision);
        await Assert.That(receipt.Token.Position).IsEqualTo(store.Position);
        var durable = OutcomeStoreOracle.Read(database.Store, operation)?.Get<CommitReceipt>();
        await Assert.That(durable is not null && JsonDefaults.Serialize(durable).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
        var outbox = ReadOutbox(store, beforeTail + 1);
        await Assert.That(outbox.Head?.Tail).IsEqualTo(beforeTail + 1);
        await Assert.That(outbox.Entry?.Sequence).IsEqualTo(beforeTail + 1);
        await Assert.That(outbox.Entry?.Ordinal).IsEqualTo(0);
        await Assert.That(outbox.Entry?.Commit).IsEqualTo(receipt.Token);
        await Assert.That(outbox.Entry?.Receipt.Kind).IsEqualTo(EventProjectionCrashScenario.MutationKind);
        await Assert.That(outbox.Entry?.Mutation is ApplyVectorProjection).IsTrue();
        var vectorBytes = outbox.Vector
            ?? throw new InvalidOperationException("The committed projection vector was missing.");
        var vector = NativeSerialization.Deserialize<VectorRecord>(vectorBytes);
        await Assert.That(vector.DocumentId).IsEqualTo(EventProjectionCrashScenario.TargetId);
        await Assert.That(vector.Values.AsSpan().SequenceEqual(new[] { 1f, 0f })).IsTrue();
        await Assert.That(vector.DocumentRevision).IsEqualTo(EventProjectionCrashScenario.DocumentRevision);
    }

    internal static async Task AssertRetryStableAsync(string root, ZoneTreeStore store, DatabaseEngine database,
        ReplicatedOperation operation, CommitReceipt first, CancellationToken cancellationToken)
    {
        var beforePosition = store.Position;
        var firstState = CaptureCommittedBytes(store, operation, ReadProjection(operation));
        var second = database.Apply(operation).Get<CommitReceipt>();
        var afterState = CaptureCommittedBytes(store, operation, ReadProjection(operation));
        await Assert.That(store.Position).IsEqualTo(beforePosition);
        await Assert.That(JsonDefaults.Serialize(second).AsSpan().SequenceEqual(JsonDefaults.Serialize(first))).IsTrue();
        await Assert.That(afterState.Vector.AsSpan().SequenceEqual(firstState.Vector)).IsTrue();
        await Assert.That(afterState.Lineage.AsSpan().SequenceEqual(firstState.Lineage)).IsTrue();
        await Assert.That(afterState.Effect.AsSpan().SequenceEqual(firstState.Effect)).IsTrue();
        await Assert.That(afterState.Outcome.AsSpan().SequenceEqual(firstState.Outcome)).IsTrue();
        await Assert.That(afterState.OutboxHead.AsSpan().SequenceEqual(firstState.OutboxHead)).IsTrue();
        await Assert.That(afterState.OutboxEntry.AsSpan().SequenceEqual(firstState.OutboxEntry)).IsTrue();
        await AssertSeedBytesAsync(root, store, includeDocuments: true, cancellationToken);
    }

    internal static ApplyVectorProjection ReadProjection(ReplicatedOperation operation)
        => JsonDefaults.Deserialize<CommandRequest>(Encoding.UTF8.GetBytes(operation.PayloadJson))
            .Mutations.Single() as ApplyVectorProjection
            ?? throw new InvalidOperationException("The persisted projection operation has an unexpected mutation.");

    internal static ProjectionCommittedBytes CaptureCommittedBytes(ZoneTreeStore store,
        ReplicatedOperation operation, ApplyVectorProjection projection)
        => store.Read(view => new ProjectionCommittedBytes(
            Required(view.ReadOwnedValue(EventProjectionCrashScenario.VectorKey(EventProjectionCrashScenario.TargetId))),
            Required(view.ReadOwnedValue(EventProjectionCrashScenario.LineageKey())),
            Required(view.ReadOwnedValue(EventProjectionCrashScenario.EffectKey(projection))),
            Required(view.ReadOwnedValue(OutcomeStoreOracle.Key(store, operation))),
            Required(view.ReadOwnedValue(EventProjectionCrashScenario.OutboxHeadKey())),
            Required(view.ReadOwnedValue(EventProjectionCrashScenario.OutboxEntryKey(
                view.GetRecord<OutboxHead>(EventProjectionCrashScenario.OutboxHeadKey())!.Tail)))));

    private static (OutboxHead? Head, OutboxEntry? Entry, byte[]? Vector) ReadOutbox(ZoneTreeStore store,
        long sequence)
        => store.Read(view =>
        {
            var head = view.GetRecord<OutboxHead>(EventProjectionCrashScenario.OutboxHeadKey());
            var entry = view.GetRecord<OutboxEntry>(EventProjectionCrashScenario.OutboxEntryKey(sequence));
            var vector = view.ReadOwnedValue(EventProjectionCrashScenario.VectorKey(EventProjectionCrashScenario.TargetId));
            return (head, entry, vector);
        });

    private static async Task AssertStoredBytesAsync(string root, ZoneTreeStore store, string fileName,
        byte[] key, CancellationToken cancellationToken)
    {
        var expected = await File.ReadAllBytesAsync(Path.Combine(root, fileName), cancellationToken);
        var actual = store.Read(view => view.ReadOwnedValue(key));
        await Assert.That(actual is not null && actual.AsSpan().SequenceEqual(expected)).IsTrue();
    }

    private static byte[] Required(byte[]? bytes)
        => bytes ?? throw new InvalidOperationException("A committed projection record was missing.");

    private static async Task AssertNoCopiedPayloadAsync(ProjectionCrashCut cut)
    {
        var lineage = cut.Lineage ?? throw new InvalidOperationException("The committed lineage record was missing.");
        var effect = cut.Effect ?? throw new InvalidOperationException("The committed projection effect was missing.");
        await Assert.That(Contains(lineage, EventProjectionCrashScenario.SourcePrivateValue)).IsFalse();
        await Assert.That(Contains(lineage, EventProjectionCrashScenario.EventPrivateValue)).IsFalse();
        await Assert.That(Contains(effect, EventProjectionCrashScenario.SourcePrivateValue)).IsFalse();
        await Assert.That(Contains(effect, EventProjectionCrashScenario.EventPrivateValue)).IsFalse();
    }

    private static bool Contains(byte[] bytes, string value)
        => bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(value)) >= 0;
}
