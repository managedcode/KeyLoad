using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Messaging;

internal static class QueuePendingAuthorityHistory
{
    internal static QueuePendingAuthorityCut Capture(ZoneTreeStore store)
        => new(store.Position, OrleansSerialization.Serialize(store.Identity), QueueWholeFlowStorage.Bytes(store));

    internal static async Task SameAsync(ZoneTreeStore store, QueuePendingAuthorityCut expected)
    {
        var actual = Capture(store);
        await Assert.That(actual.Position).IsEqualTo(expected.Position);
        await Assert.That(actual.Identity.SequenceEqual(expected.Identity)).IsTrue();
        await Assert.That(actual.Rows).IsEquivalentTo(expected.Rows, CollectionOrdering.Matching);
    }

    internal static async Task RefuseAsync(DatabaseEngine owner, ZoneTreeStore store, QueuePendingAuthorityState state,
        QueuePendingAuthorityCut damaged)
    {
        await MissingOutcomeAsync(store, state);
        var error = Assert.ThrowsExactly<KeyLoadException>(() => owner.Apply(state.Park));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(error.Message).IsEqualTo(QueuePendingAuthorityProtocol.MissingReference);
        await MissingOutcomeAsync(store, state);
        await QueuePendingAuthorityEffects.RequireAsync(owner, store, state);
        await SameAsync(store, damaged);
    }

    private static async Task MissingOutcomeAsync(ZoneTreeStore store, QueuePendingAuthorityState state)
        => await Assert.That(store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(
            state.Flow.Partition, QueueLifecycleTestProtocol.Administrator, state.Park.CommandId)))).IsNull();

    internal static async Task ReplayAsync(DatabaseEngine owner, ZoneTreeStore store, QueuePendingAuthorityState state)
    {
        var original = Capture(store);
        foreach (var (operation, result) in state.Flow.Successes)
        {
            var saved = store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(
                state.Flow.Partition, QueueLifecycleTestProtocol.Administrator, operation.CommandId)))!;
            await Assert.That(saved.Incarnation).IsEqualTo(store.Identity.Incarnation);
            await Assert.That(OrleansSerialization.Serialize(saved.Result).SequenceEqual(OrleansSerialization.Serialize(result))).IsTrue();
            var replay = owner.Apply(operation);
            if (operation.Value!.Kind == OperationKind.Receive)
            {
                await Assert.That(Errors.Code(replay.Problem).Value).IsEqualTo(ErrorCode.StaleLease);

                await Assert.That(replay.Value).IsNull();
            }
            else
            {
                await Assert.That(OrleansSerialization.Serialize(replay).SequenceEqual(OrleansSerialization.Serialize(result))).IsTrue();
            }
        }
        await SameAsync(store, original);
    }
}
