using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

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
        var identity = NativeSerialization.Serialize(store.Identity);
        await Assert.That(OutcomeStoreOracle.Read(store, state.Attempt)).IsEqualTo(refusal);
        var locator = store.Read(view => view.ReadOwnedValue(KeySpace.OutcomeLocatorV2(state.Lane.Partition,
            state.Attempt.PrincipalId, state.Attempt.Id)));
        await Assert.That(locator).IsNotNull();
        await Assert.That(locator!).IsEquivalentTo(KeySpace.PartitionOutcome(state.Lane.Partition,
            state.Attempt.PrincipalId, state.Attempt.Id), TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(database.Apply(state.Attempt)).IsEqualTo(refusal);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
        await Assert.That(database.ResolveOutcome(state.Attempt)).IsEqualTo(refusal);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
        foreach (var item in state.Outcomes)
        {
            token.ThrowIfCancellationRequested();
            await Assert.That(NativeSerialization.Serialize(OutcomeStoreOracle.Read(store, item.Operation)))
                .IsEquivalentTo(NativeSerialization.Serialize(item.Result), TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }
    }

    internal static string[] Domain(ZoneTreeStore store, QueueLaneRef lane, Command<DatabaseCommand> operation)
    {
        var outcome = Convert.ToHexString(OutcomeStoreOracle.Key(store, operation)) + QueueDeadlineCapacityProtocol.ByteSeparator;
        var locator = Convert.ToHexString(KeySpace.OutcomeLocatorV2(lane.Partition, operation.UserId!,
            operation.CommandId)) + QueueDeadlineCapacityProtocol.ByteSeparator;
        var clock = Convert.ToHexString(KeySpace.ClockBytes) + QueueDeadlineCapacityProtocol.ByteSeparator;
        return QueueWholeFlowStorage.Bytes(store).Where(row => !row.StartsWith(outcome, StringComparison.Ordinal)
            && !row.StartsWith(clock, StringComparison.Ordinal) && !row.StartsWith(locator, StringComparison.Ordinal)).ToArray();
    }
}
