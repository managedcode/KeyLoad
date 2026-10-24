using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Messaging;

internal static class QueueDeadLetterAdmissionAssertions
{
    private const string InvalidCounter = "The queue lifecycle accounting is invalid.";

    internal static async Task Refuse(DatabaseEngine owner, ZoneTreeStore store, QueueDeadLetterSelectedState state,
        QueueDeadLetterAdmissionFault fault, long position, string[] image)
    {
        var identity = store.Identity;
        var inspection = Assert.ThrowsExactly<KeyLoadException>(() => owner.InspectMessage(
            QueueDeadLetterSelectedProtocol.Administrator, state.Lane, state.Target));
        var cancellation = Assert.ThrowsExactly<KeyLoadException>(() => owner.Apply(state.Original));
        var detail = fault == QueueDeadLetterAdmissionFault.MissingWitnessMetadata
            ? QueueDeadLetterSelectedProtocol.MissingMetadataDetail : InvalidCounter;
        await Assert.That(inspection.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(inspection.Message).IsEqualTo(detail);
        await Assert.That(cancellation.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(cancellation.Message).IsEqualTo(detail);
        await Assert.That(store.Identity).IsEqualTo(identity);
        await Image(store, position, image);
        await Assert.That(store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(state.Lane.Partition,
            QueueDeadLetterSelectedProtocol.Administrator, state.Original.CommandId)))).IsNull();
        await QueueDeadLetterSelectedRetainedInbox.RequireAsync(owner, store, state);
        await Image(store, position, image);
    }

    internal static async Task Image(ZoneTreeStore store, long position, string[] image)
    {
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }
}
