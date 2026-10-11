using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadLetterSelectedHistory
{
    internal static async Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store, QueueDeadLetterSelectedState state,
        IEnumerable<QueueDeadLetterSelectedSaved> history)
    {
        var before = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        foreach (var saved in history)
        {
            var retained = store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(
                state.Lane.Partition, QueueDeadLetterSelectedProtocol.Administrator, saved.Operation.Id)))!;
            if (saved.Operation.Kind == OperationKind.Receive)
            {
                await NativeReplayResultAssertions.Same<ReceiveResult>(retained.Result, saved.Result);
                var stale = database.Apply(saved.Operation);
                await Assert.That(stale.Error).IsEqualTo(ErrorCode.StaleLease);
                await Assert.That(stale.Json).IsNull();
                await Assert.That(stale.NativeValue).IsNull();
            }
            else
            {
                await NativeReplayResultAssertions.Same<CommitReceipt>(retained.Result, saved.Result);
                await NativeReplayResultAssertions.Same<CommitReceipt>(database.Apply(saved.Operation), saved.Result);
            }
        }
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(before, CollectionOrdering.Matching);
    }
}
