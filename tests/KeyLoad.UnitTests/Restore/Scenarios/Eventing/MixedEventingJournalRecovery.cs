using ManagedCode.Communication.Commands;
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
        var operation = source.Database.CreateCommand(OperationKind.Batch, id, QueueLifecycleTestProtocol.Administrator, state.Time, JsonSerializer.Serialize(command, JsonDefaults.Options));
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => source.SubmitIssued(operation));
        originalFailures.Add(failure);
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(source.Store.Position).IsEqualTo(before);
        await QueueLifecycleImage.SameAsync(source.Store, state.Lane, corrupt);
        await Assert.That(QueueWholeFlowStorage.Bytes(source.Store))
            .IsEquivalentTo(corruptRaw, CollectionOrdering.Matching);
        var retained = source.ReadStoredOutcome(id);
        var journal = source.ReadCanonicalCut();
        await Assert.That(journal.LastIndex).IsEqualTo(retained.CanonicalSequence);
        await Assert.That(journal.CommittedIndex).IsEqualTo(retained.CanonicalSequence);
        await Assert.That(journal.Term).IsEqualTo(retained.Term);
        var outcomeKey = KeySpace.PartitionOutcome(state.Partition, operation.UserId!, id);
        await Assert.That(source.Store.Read(view => view.ReadOwnedValue(outcomeKey))).IsNull();
        await RetainedRefusalAsync(source, state, operation, originalFailures, corruptRaw, before);
        var repairedCut = before;
        var applied = source.RecoverFaultOwnedRow(operation,
            () =>
            {
                source.Store.Commit((transaction, _) => { transaction.Put(key, exact); return true; });
                repairedCut = source.Store.Position;
            },
            originalFailures, cancellationToken);
        await Assert.That(applied.Index).IsEqualTo(retained.CanonicalSequence);
        await Assert.That(applied.Term).IsEqualTo(retained.Term);
        await Assert.That(applied.Operation!.CommandId).IsEqualTo(id);
        await Assert.That(applied.Operation.Value!.Input.ToArray()).IsEquivalentTo(
            retained.PreparedCommand!.Value!.Input.ToArray(), CollectionOrdering.Matching);
        await Assert.That(source.Database.LastApplied).IsEqualTo(retained.CanonicalSequence);
        await QueueLifecycleImage.SameAsync(source.Store, state.Lane, whole);
        await QueueLifecycleAccountingAssertions.InitialAsync(source.Store, state);
        await MixedEventingFailurePostimage.RequireAsync(source, state, operation, corruptRaw, key, exact,
            outcomeKey, repairedCut, retained.CanonicalSequence);
        await RequireOutcomeAsync(source, state, operation, outcomeKey, retained.CanonicalSequence, retained.Term);
    }

    private static async Task RetainedRefusalAsync(TestDatabase source, QueueLifecycleTestState state,
        Command<DatabaseCommand> operation, List<Exception> originalFailures, string[] corruptRaw, long position)
    {
        var journal = source.ReadCanonicalCut();
        var retained = NativeSerialization.Serialize(source.ReadStoredOutcome(operation.CommandId));
        var identity = NativeSerialization.Serialize(source.Store.Identity);
        var repeated = Assert.ThrowsExactly<KeyLoadException>(() => source.SubmitIssued(operation));
        originalFailures.Add(repeated);
        await Assert.That(repeated.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(source.ReadCanonicalCut()).IsEqualTo(journal);
        await Assert.That(NativeSerialization.Serialize(source.ReadStoredOutcome(operation.CommandId)))
            .IsEquivalentTo(retained, CollectionOrdering.Matching);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(source.Store, corruptRaw, position, identity);
        await Assert.That(source.Store.Read(view => view.ReadOwnedValue(
            KeySpace.PartitionOutcome(state.Partition, operation.UserId!, operation.CommandId)))).IsNull();
    }

    private static async Task RequireOutcomeAsync(TestDatabase source, QueueLifecycleTestState state,
        Command<DatabaseCommand> operation, byte[] outcomeKey, long index, long term)
    {
        var expected = source.Database.ResolveOutcome(operation);
        await Assert.That(Errors.Code(expected.Problem)).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(expected.Problem?.Detail).IsEqualTo(FullDetail);
        
        await Assert.That(expected.Value).IsNull();
        var exact = source.Store.Read(view => view.ReadOwnedValue(outcomeKey))!;
        var cut = source.Store.Position;
        var replay = source.SubmitIssued(operation);
        await Assert.That(Errors.Code(replay.Problem)).IsEqualTo(Errors.Code(expected.Problem));
        await Assert.That(replay.Problem?.Detail).IsEqualTo(FullDetail);
        
        await Assert.That(replay.Value).IsNull();
        await Assert.That(source.Store.Position).IsEqualTo(cut);
        await Assert.That(source.ReadStoredOutcome(operation.CommandId).CanonicalSequence).IsEqualTo(index);
        await Assert.That(source.ReadStoredOutcome(operation.CommandId).Term).IsEqualTo(term);
        await Assert.That(source.Store.Read(view => view.ReadOwnedValue(outcomeKey)))
            .IsEquivalentTo(exact, CollectionOrdering.Matching);
        await ChangedAsync(source, state, operation, cut);
        state.Failures.Add((operation, expected));
    }
    private static async Task ChangedAsync(TestDatabase source, QueueLifecycleTestState state,
        Command<DatabaseCommand> original, long cut)
    {
        var journal = source.ReadCanonicalCut();
        var whole = QueueWholeFlowStorage.Bytes(source.Store);
        var changed = new CommandRequest(original.CommandId, state.Partition, [new ParkPendingQueueMessage(state.Lane.Queue,
            QueueLifecycleTestProtocol.Pending, QueueLifecycleTestProtocol.Two, QueueLifecycleTestProtocol.One)]);
        var refused = source.SubmitIssued(source.Database.CreateCommand(original.Value!.Kind, original.CommandId, original.UserId!, new DateTimeOffset(original.Timestamp), JsonSerializer.Serialize(changed, JsonDefaults.Options)));
        await Assert.That(Errors.Code(refused.Problem)).IsEqualTo(ErrorCode.Conflict);
        
        await Assert.That(refused.Value).IsNull();
        await Assert.That(source.Store.Position).IsEqualTo(cut);
        await Assert.That(source.ReadCanonicalCut()).IsEqualTo(journal);
        await Assert.That(QueueWholeFlowStorage.Bytes(source.Store))
            .IsEquivalentTo(whole, CollectionOrdering.Matching);
    }

}
