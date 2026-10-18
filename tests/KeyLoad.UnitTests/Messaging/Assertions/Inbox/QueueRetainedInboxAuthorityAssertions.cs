using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueRetainedInboxAuthorityAssertions
{
    private const string InvalidBinary = "The internal binary payload is invalid.";
    private const string StaleDelivery = "The delivery lease is stale.";

    internal static async Task OnlySelectedRowChangedAsync(QueuePendingAuthorityCut original, QueuePendingAuthorityCut damaged,
        byte[] key, byte[] malformed)
    {
        await Assert.That(damaged.Position).IsEqualTo(original.Position + 1);
        await Assert.That(damaged.Identity.SequenceEqual(original.Identity)).IsTrue();
        await Assert.That(damaged.Rows.Length).IsEqualTo(original.Rows.Length);
        var prefix = Convert.ToHexString(key) + ":";
        var prior = original.Rows.Single(row => row.StartsWith(prefix, StringComparison.Ordinal));
        var changed = prefix + Convert.ToHexString(malformed);
        await Assert.That(damaged.Rows.Except(original.Rows)).IsEquivalentTo([changed], CollectionOrdering.Matching);
        await Assert.That(original.Rows.Except(damaged.Rows)).IsEquivalentTo([prior], CollectionOrdering.Matching);
    }

    internal static async Task RefuseAsync(DatabaseEngine owner, ZoneTreeStore store, QueuePendingAuthorityState state,
        Command<DatabaseCommand> equivalent, QueuePendingAuthorityCut damaged)
    {
        await Assert.That(Outcome(store, state, equivalent)).IsNull();
        var error = Assert.ThrowsExactly<KeyLoadException>(() => owner.Apply(equivalent));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(error.Message).IsEqualTo(InvalidBinary);
        await Assert.That(Outcome(store, state, equivalent)).IsNull();
        await QueuePendingAuthorityHistory.SameAsync(store, damaged);
        await QueuePendingAuthorityHistory.ReplayAsync(owner, store, state);
        await QueuePendingAuthorityHistory.SameAsync(store, damaged);
    }

    internal static async Task EquivalentAsync(DatabaseEngine owner, ZoneTreeStore store, QueuePendingAuthorityState state,
        Command<DatabaseCommand> equivalent, QueuePendingAuthorityProcessed history, byte[] key, byte[] exact)
    {
        var lane = QueueLifecycleImage.Capture(store, state.Flow.Lane);
        var before = store.Position;
        var result = owner.Apply(equivalent);
        await Assert.That(NativeSerialization.Serialize(result.Get<CommitReceipt>())
            .SequenceEqual(NativeSerialization.Serialize(history.Receipt))).IsTrue();
        await Assert.That(result.Get<CommitReceipt>().CommandId).IsEqualTo(history.Request.CommandId);
        await Assert.That(store.Position).IsEqualTo(before + 1);
        await Assert.That(Outcome(store, state, equivalent)).IsNotNull();
        state.Flow.Successes.Add((equivalent, result));
        await QueueLifecycleImage.SameAsync(store, state.Flow.Lane, lane);
        await Assert.That(store.Read(view => view.ReadOwnedValue(key))!.SequenceEqual(exact)).IsTrue();
        await QueuePendingAuthorityImage.RequireAsync(owner, store, state, QueuePendingAuthorityPhase.Pending);
        await QueuePendingAuthorityHistory.ReplayAsync(owner, store, state);
    }

    internal static async Task<Result<object>> NewGenerationAsync(DatabaseEngine owner, ZoneTreeStore store,
        QueuePendingAuthorityState state, Command<DatabaseCommand> operation)
    {
        var lane = QueueLifecycleImage.Capture(store, state.Flow.Lane);
        var before = store.Position;
        var failed = owner.Apply(operation);
        await Assert.That(Errors.Code(failed.Problem)).IsEqualTo(ErrorCode.StaleLease);
        await Assert.That(failed.Problem?.Detail).IsEqualTo(StaleDelivery);
        
        await Assert.That(failed.Value).IsNull();
        await Assert.That(store.Position).IsEqualTo(before + 1);
        await QueueLifecycleImage.SameAsync(store, state.Flow.Lane, lane);
        await QueuePendingAuthorityImage.RequireAsync(owner, store, state, QueuePendingAuthorityPhase.Pending);
        await FailureReplayAsync(owner, store, state, operation, failed);
        return failed;
    }

    internal static async Task FailureReplayAsync(DatabaseEngine owner, ZoneTreeStore store, QueuePendingAuthorityState state,
        Command<DatabaseCommand> operation, Result<object> failed)
    {
        var before = QueuePendingAuthorityHistory.Capture(store);
        var saved = Outcome(store, state, operation)!;
        await Assert.That(saved.Incarnation).IsEqualTo(store.Identity.Incarnation);
        await Assert.That(NativeSerialization.Serialize(saved.Result).SequenceEqual(NativeSerialization.Serialize(failed))).IsTrue();
        await Assert.That(NativeSerialization.Serialize(owner.Apply(operation)).SequenceEqual(NativeSerialization.Serialize(failed))).IsTrue();
        await QueuePendingAuthorityHistory.SameAsync(store, before);
        var history = state.Processed.First();
        await Assert.That(store.Read(view => view.GetRecord<InboxRecord>(QueuePendingAuthorityOperations.Key(state.Flow,
            QueuePendingAuthorityProtocol.Inbox, history.Id, history.Request.HandlerScope, 2)))).IsNull();
    }

    private static StoredOutcome? Outcome(ZoneTreeStore store, QueuePendingAuthorityState state, Command<DatabaseCommand> operation)
        => store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(state.Flow.Partition,
            QueueLifecycleTestProtocol.Administrator, operation.CommandId)));
}
