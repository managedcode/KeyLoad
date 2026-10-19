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
                state.Lane.Partition, QueueDeadLetterSelectedProtocol.Administrator, saved.Operation.CommandId)))!;
            if (saved.Operation.Value!.Kind == OperationKind.Receive)
            {
                await ReplayResultAssertions.Same<ReceiveResult>(retained.Result, saved.Result);
                var stale = database.Apply(saved.Operation);
                await Assert.That(Errors.Code(stale.Problem)).IsEqualTo(ErrorCode.StaleLease);

                await Assert.That(stale.Value).IsNull();
            }
            else
            {
                await ReplayResultAssertions.Same<CommitReceipt>(retained.Result, saved.Result);
                await ReplayResultAssertions.Same<CommitReceipt>(database.Apply(saved.Operation), saved.Result);
            }
        }
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(before, CollectionOrdering.Matching);
    }
}
