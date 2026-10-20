using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueScheduledAuthorityHistory
{
    internal static async Task OneRowAsync(QueuePendingAuthorityCut original, QueuePendingAuthorityCut damaged,
        byte[] key, byte[] value)
    {
        await Assert.That(damaged.Position).IsEqualTo(original.Position + 1);
        await Assert.That(damaged.Identity.SequenceEqual(original.Identity)).IsTrue();
        await Assert.That(damaged.Rows.Length).IsEqualTo(original.Rows.Length);
        var prefix = Convert.ToHexString(key) + ":";
        await Assert.That(damaged.Rows.Except(original.Rows)).IsEquivalentTo([prefix + Convert.ToHexString(value)], CollectionOrdering.Matching);
        await Assert.That(original.Rows.Except(damaged.Rows)).IsEquivalentTo(
            [original.Rows.Single(row => row.StartsWith(prefix, StringComparison.Ordinal))], CollectionOrdering.Matching);
    }

    internal static async Task RefuseAsync(DatabaseEngine owner, ZoneTreeStore store, QueueScheduledAuthorityState state,
        QueuePendingAuthorityCut damaged)
    {
        await Assert.That(Outcome(store, state, state.Receive.CommandId)).IsNull();
        var error = Assert.ThrowsExactly<KeyLoadException>(() => owner.Apply(state.Receive));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(error.Message).IsEqualTo("The recorded queue retry decision no longer matches its input.");
        await Assert.That(Outcome(store, state, state.Receive.CommandId)).IsNull();
        await QueuePendingAuthorityHistory.SameAsync(store, damaged);
        await ReplayAsync(owner, store, state);
        await QueuePendingAuthorityHistory.SameAsync(store, damaged);
    }

    internal static async Task ReplayAsync(DatabaseEngine owner, ZoneTreeStore store, QueueScheduledAuthorityState state)
    {
        var before = QueuePendingAuthorityHistory.Capture(store);
        foreach (var (operation, result) in state.Flow.Successes)
        {
            var saved = Outcome(store, state, operation.CommandId)!;
            await Assert.That(saved.Incarnation).IsEqualTo(store.Identity.Incarnation);
            await Assert.That(OrleansSerialization.Serialize(saved.Result).SequenceEqual(OrleansSerialization.Serialize(result))).IsTrue();
            var replay = owner.Apply(operation);
            if (operation.Value!.Kind == OperationKind.Receive)
            {
                await Assert.That(Errors.Code(replay.Problem)).IsEqualTo(ErrorCode.StaleLease);

                await Assert.That(replay.Value).IsNull();
            }
            else
            {
                await Assert.That(OrleansSerialization.Serialize(replay).SequenceEqual(OrleansSerialization.Serialize(result))).IsTrue();
            }
        }
        await QueuePendingAuthorityHistory.SameAsync(store, before);
    }

    private static StoredOutcome? Outcome(ZoneTreeStore store, QueueScheduledAuthorityState state, Guid id)
        => store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(state.Flow.Partition,
            QueueLifecycleTestProtocol.Administrator, id)));
}
