using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadLetterSelectedRefusal
{
    internal static async Task RequireAsync(DatabaseEngine database, ZoneTreeStore store,
        QueueDeadLetterSelectedState state, string[] damaged)
    {
        var position = store.Position;
        var identity = store.Identity;
        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
        {
            if (state.Kind == QueueDeadLetterSelectedOperation.Read)
            {
                _ = database.InspectMessage(QueueDeadLetterSelectedProtocol.Administrator, state.Lane, state.Target);
            }
            else
            {
                _ = database.Apply(state.Original);
            }
            throw new InvalidOperationException(QueueDeadLetterSelectedProtocol.ReturnedRefusal);
        });
        await Assert.That(error.Code).IsEqualTo(state.Fault is QueueDeadLetterSelectedFault.IndexAlias or QueueDeadLetterSelectedFault.WitnessMetadataAlias
            ? ErrorCode.RecoveryRequired : ErrorCode.Corruption);
        var detail = state.Fault switch
        {
            QueueDeadLetterSelectedFault.MissingBody or QueueDeadLetterSelectedFault.ForeignBody
                => QueueDeadLetterSelectedProtocol.MissingBodyDetail,
            QueueDeadLetterSelectedFault.WitnessMetadataAlias => QueueDeadLetterSelectedProtocol.MissingMetadataDetail,
            _ => QueueDeadLetterSelectedProtocol.MissingReference
        };
        await Assert.That(error.Message).IsEqualTo(detail);
        await Assert.That(store.Identity).IsEqualTo(identity);
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(damaged, CollectionOrdering.Matching);
        await Assert.That(store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(state.Lane.Partition,
            QueueDeadLetterSelectedProtocol.Administrator, state.Original.CommandId)))).IsNull();
        await QueueDeadLetterSelectedRetainedInbox.RequireAsync(database, store, state);
    }
}
