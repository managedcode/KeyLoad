using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Messaging;

internal static class QueueDeadlineCapacityRefusal
{
    internal static async Task RequireAsync(DatabaseEngine database, ZoneTreeStore store,
        QueueDeadlineCapacityState state, Result<object> refusal, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var metadata = state.Fault == QueueDeadlineCapacityFault.StateVersion
            ? state.Original with
            {
                StateVersion = long.MaxValue
            } : state.Original;
        var sequence = state.Fault == QueueDeadlineCapacityFault.ReadySequence ? long.MaxValue : state.Counters.NextReadySequence;
        await QueueDeadlineCapacityImage.RequireAsync(database, state.Lane, [(state.Input, metadata)], sequence,
            state.Fault == QueueDeadlineCapacityFault.ParkedSequence ? long.MaxValue : state.Counters.NextParkedSequence);
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        var original = OutcomeStoreOracle.Read(store, state.Attempt)
            ?? throw new InvalidOperationException("The failed outcome is absent.");
        await Assert.That(OrleansSerialization.Serialize(original)).IsEquivalentTo(OrleansSerialization.Serialize(refusal),
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        var locator = store.Read(view => view.ReadOwnedValue(KeySpace.OutcomeLocator(state.Lane.Partition,
            state.Attempt.UserId!, state.Attempt.CommandId)));
        await Assert.That(locator).IsNotNull();
        await Assert.That(locator!).IsEquivalentTo(KeySpace.PartitionOutcome(state.Lane.Partition,
            state.Attempt.UserId!, state.Attempt.CommandId), TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(database.Apply(state.Attempt)).IsEqualTo(refusal);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
        await Assert.That(database.ResolveOutcome(state.Attempt)).IsEqualTo(refusal);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
        foreach (var item in state.Outcomes)
        {
            token.ThrowIfCancellationRequested();
            var stored = OutcomeStoreOracle.Read(store, item.Operation)
                ?? throw new InvalidOperationException("The original outcome is absent.");
            await Assert.That(OrleansSerialization.Serialize(stored))
                .IsEquivalentTo(OrleansSerialization.Serialize(item.Result), TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }
    }

    internal static string[] Domain(ZoneTreeStore store, QueueLaneRef lane, Command<DatabaseCommand> operation)
    {
        var outcome = Convert.ToHexString(OutcomeStoreOracle.Key(operation)) + QueueDeadlineCapacityProtocol.ByteSeparator;
        var locator = Convert.ToHexString(KeySpace.OutcomeLocator(lane.Partition, operation.UserId!,
            operation.CommandId)) + QueueDeadlineCapacityProtocol.ByteSeparator;
        var clock = Convert.ToHexString(KeySpace.ClockBytes) + QueueDeadlineCapacityProtocol.ByteSeparator;
        var applied = Convert.ToHexString(KeySpace.AppliedBytes) + QueueDeadlineCapacityProtocol.ByteSeparator;
        return QueueWholeFlowStorage.Bytes(store).Where(row => !row.StartsWith(outcome, StringComparison.Ordinal)
            && !row.StartsWith(clock, StringComparison.Ordinal)
            && !row.StartsWith(applied, StringComparison.Ordinal) && !row.StartsWith(locator, StringComparison.Ordinal)).ToArray();
    }
}
