using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class MixedEventingJournalRecovery
{
    private const string FullDetail = "The queue dead-letter sublimit is exhausted.";

    internal static async Task RunAsync(TestDatabase source, QueueLifecycleTestState state,
        List<Exception> originalFailures, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = KeySpace.Partition(QueueLifecycleTestProtocol.ParkedSpace, state.Partition, state.Lane.Queue,
            QueueLifecycleTestProtocol.One, QueueLifecycleTestProtocol.Parked);
        var exact = source.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException(QueueLifecycleTestProtocol.BodyFixtureMismatch);
        var whole = QueueLifecycleImage.Capture(source.Store, state.Lane);
        source.Store.Commit((transaction, _) => { transaction.Delete(key); return true; });
        var corrupt = QueueLifecycleImage.Capture(source.Store, state.Lane);
        var before = source.Store.Position;
        var corruptRaw = QueueWholeFlowStorage.Bytes(source.Store);
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, state.Partition, [new ParkPendingQueueMessage(state.Lane.Queue,
            QueueLifecycleTestProtocol.Pending, QueueLifecycleTestProtocol.Three, QueueLifecycleTestProtocol.One)]);
        var operation = new ReplicatedOperation(id, OperationKind.Batch, QueueLifecycleTestProtocol.Administrator,
            state.Time, JsonSerializer.Serialize(command, JsonDefaults.Options));
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => source.SubmitIssued(operation));
        originalFailures.Add(failure);
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(source.Store.Position).IsEqualTo(before);
        await QueueLifecycleImage.SameAsync(source.Store, state.Lane, corrupt);
        await Assert.That(QueueWholeFlowStorage.Bytes(source.Store))
            .IsEquivalentTo(corruptRaw, CollectionOrdering.Matching);
        var retained = source.ReadRetainedEntry(id);
        var journal = source.ReadJournalCut();
        await Assert.That(journal.LastIndex).IsEqualTo(retained.Index);
        await Assert.That(journal.CommittedIndex).IsEqualTo(retained.Index);
        await Assert.That(journal.Term).IsEqualTo(retained.Term);
        var outcomeKey = KeySpace.PartitionOutcome(state.Partition, operation.PrincipalId, id);
        await Assert.That(source.Store.Read(view => view.ReadOwnedValue(outcomeKey))).IsNull();
        var repairedCut = before;
        var applied = source.RecoverFaultOwnedRow(operation,
            () =>
            {
                source.Store.Commit((transaction, _) => { transaction.Put(key, exact); return true; });
                repairedCut = source.Store.Position;
            },
            originalFailures, cancellationToken);
        await Assert.That(applied.Index).IsEqualTo(retained.Index);
        await Assert.That(applied.Term).IsEqualTo(retained.Term);
        await Assert.That(applied.Operation!.Id).IsEqualTo(id);
        await Assert.That(applied.Operation.NativePayload.ToArray()).IsEquivalentTo(
            retained.Operation!.NativePayload.ToArray(), CollectionOrdering.Matching);
        await Assert.That(source.Database.LastApplied).IsEqualTo(retained.Index);
        await QueueLifecycleImage.SameAsync(source.Store, state.Lane, whole);
        await QueueLifecycleAccountingAssertions.InitialAsync(source.Store, state);
        await MixedEventingFailurePostimage.RequireAsync(source, state, operation, corruptRaw, key, exact,
            outcomeKey, repairedCut, retained.Index);
        await RequireOutcomeAsync(source, state, operation, outcomeKey, retained.Index, retained.Term);
    }

    private static async Task RequireOutcomeAsync(TestDatabase source, QueueLifecycleTestState state,
        ReplicatedOperation operation, byte[] outcomeKey, long index, long term)
    {
        var expected = source.Database.ResolveOutcome(operation);
        await Assert.That(expected.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(expected.SafeDetail).IsEqualTo(FullDetail);
        await Assert.That(expected.Json).IsNull();
        await Assert.That(expected.NativeValue).IsNull();
        var exact = source.Store.Read(view => view.ReadOwnedValue(outcomeKey))!;
        var cut = source.Store.Position;
        var replay = source.SubmitIssued(operation);
        await Assert.That(replay.Error).IsEqualTo(expected.Error);
        await Assert.That(replay.SafeDetail).IsEqualTo(FullDetail);
        await Assert.That(replay.Json).IsNull();
        await Assert.That(replay.NativeValue).IsNull();
        await Assert.That(source.Store.Position).IsEqualTo(cut);
        await Assert.That(source.ReadRetainedEntry(operation.Id).Index).IsEqualTo(index);
        await Assert.That(source.ReadRetainedEntry(operation.Id).Term).IsEqualTo(term);
        await Assert.That(source.Store.Read(view => view.ReadOwnedValue(outcomeKey)))
            .IsEquivalentTo(exact, CollectionOrdering.Matching);
        await ChangedAsync(source, state, operation, cut);
        state.Failures.Add((operation, expected));
    }
    private static async Task ChangedAsync(TestDatabase source, QueueLifecycleTestState state,
        ReplicatedOperation original, long cut)
    {
        var journal = source.ReadJournalCut();
        var whole = QueueWholeFlowStorage.Bytes(source.Store);
        var changed = new CommandRequest(original.Id, state.Partition, [new ParkPendingQueueMessage(state.Lane.Queue,
            QueueLifecycleTestProtocol.Pending, QueueLifecycleTestProtocol.Two, QueueLifecycleTestProtocol.One)]);
        var refused = source.SubmitIssued(original with
        { PayloadJson = JsonSerializer.Serialize(changed, JsonDefaults.Options) });
        await Assert.That(refused.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(refused.Json).IsNull();
        await Assert.That(refused.NativeValue).IsNull();
        await Assert.That(source.Store.Position).IsEqualTo(cut);
        await Assert.That(source.ReadJournalCut()).IsEqualTo(journal);
        await Assert.That(QueueWholeFlowStorage.Bytes(source.Store))
            .IsEquivalentTo(whole, CollectionOrdering.Matching);
    }

}
