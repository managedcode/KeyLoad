using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Messaging;

internal static class QueueMissingMetadataAssertions
{
    internal static async Task RefusalAsync(DatabaseEngine owner, ZoneTreeStore store, QueueLifecycleTestState state,
        Command<DatabaseCommand> operation, QueueLifecycleConditionCut cut)
    {
        var outcomeKey = KeySpace.PartitionOutcome(state.Partition, QueueLifecycleTestProtocol.Administrator, operation.CommandId);
        for (var repeat = 0; repeat < 2; repeat++)
        {
            var inspected = Assert.ThrowsExactly<KeyLoadException>(() => owner.InspectMessage(
                QueueLifecycleTestProtocol.Administrator, state.Lane, QueueLifecycleConditionOperations.Source));
            await Assert.That(inspected.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(inspected.Message).IsEqualTo("The queue lifecycle metadata is unavailable.");
            await QueueLifecycleConditionAssertions.Same(store, cut);
            await Assert.That(store.Read(view => view.GetRecord<StoredOutcome>(outcomeKey))).IsNull();
            var received = Assert.ThrowsExactly<KeyLoadException>(() => owner.Apply(operation));
            await Assert.That(received.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(received.Message).IsEqualTo("Queue metadata is absent.");
            await Assert.That(store.Read(view => view.GetRecord<StoredOutcome>(outcomeKey))).IsNull();
            await QueueLifecycleConditionAssertions.Same(store, cut);
            await AbsentAsync(owner, store, state);
        }
    }

    internal static async Task TerminalAsync(DatabaseEngine owner, ZoneTreeStore store, QueueLifecycleTestState state)
    {
        await QueueTerminalInspectionAssertions.TerminalAsync(owner, store, state,
            QueueTerminalInspectionAssertions.Expected(MessageState.Acked, state), healthy: true);
        var metadata = store.Read(view => view.Scan(KeySpace.Partition(
            QueueLifecycleTestProtocol.MetadataSpace, state.Partition, state.Lane.Queue), 3));
        await Assert.That(metadata.Records.Length).IsEqualTo(2);
        await Assert.That(metadata.HasMore).IsFalse();
        await AbsentAsync(owner, store, state);
    }

    private static async Task AbsentAsync(DatabaseEngine owner, ZoneTreeStore store, QueueLifecycleTestState state)
    {
        var before = QueueLifecycleConditionAssertions.Capture(store);
        await Assert.That(owner.InspectMessage(QueueLifecycleTestProtocol.Administrator,
            state.Lane, "truly-absent-message")).IsNull();
        await QueueLifecycleConditionAssertions.Same(store, before);
    }
}
