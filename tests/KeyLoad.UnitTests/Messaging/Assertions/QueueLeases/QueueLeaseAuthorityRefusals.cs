using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLeaseAuthorityRefusals
{
    internal static async Task RequireAsync(DatabaseEngine database, ZoneTreeStore store,
        QueueLeaseAuthorityState state, QueueLeaseAuthorityFault fault, string[] image)
    {
        var position = store.Position;
        var detail = Detail(fault);
        var settlement = Assert.ThrowsExactly<KeyLoadException>(() => database.Apply(state.Original));
        await Assert.That(settlement.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(settlement.Message).IsEqualTo(detail);
        await UnchangedAsync(store, position, image);
        var replay = Assert.ThrowsExactly<KeyLoadException>(() => database.Apply(state.Claim));
        await Assert.That(replay.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(replay.Message).IsEqualTo(detail);
        await UnchangedAsync(store, position, image);
        var resolved = database.ResolveOutcome(state.Claim);
        await Assert.That(Errors.Code(resolved.Problem)).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(resolved.Problem?.Detail).IsEqualTo(detail);

        await Assert.That(resolved.Value).IsNull();
        await UnchangedAsync(store, position, image);
        var outcome = store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(
            state.Lane.Partition, QueueLeaseAuthorityProtocol.Root, state.Original.CommandId)));
        await Assert.That(outcome).IsNull();
        await Assert.That(database.GetDocument(QueueLeaseAuthorityProtocol.Root,
            new(state.Lane.Partition, QueueLeaseAuthorityProtocol.Collection, QueueLeaseAuthorityProtocol.Document))).IsNull();
        var inbox = store.Read(view => view.GetRecord<InboxRecord>(KeySpace.Partition(QueueLeaseAuthorityProtocol.InboxSpace,
            state.Lane.Partition, state.Lane.Queue, state.Message.MessageId, QueueLeaseAuthorityProtocol.Handler, QueueLeaseAuthorityProtocol.One)));
        await Assert.That(inbox).IsNull();
    }

    private static string Detail(QueueLeaseAuthorityFault fault)
    {
        if (fault is QueueLeaseAuthorityFault.NegativeAttempts or QueueLeaseAuthorityFault.ZeroAttempts
            or QueueLeaseAuthorityFault.NegativeStateVersion or QueueLeaseAuthorityFault.ZeroStateVersion
            or QueueLeaseAuthorityFault.UnclaimedStateVersion or QueueLeaseAuthorityFault.NegativeReadySequence
            or QueueLeaseAuthorityFault.ZeroReadySequence)
        {
            return QueueLeaseAuthorityProtocol.InvalidMetadataDetail;
        }
        return fault is QueueLeaseAuthorityFault.ForeignBodyIdentity or QueueLeaseAuthorityFault.ForeignMetadataIdentity
            ? QueueLeaseAuthorityProtocol.MissingBodyDetail : QueueLifecycleProtocol.MissingReference;
    }

    private static async Task UnchangedAsync(ZoneTreeStore store, long position, string[] image)
    {
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }
}
