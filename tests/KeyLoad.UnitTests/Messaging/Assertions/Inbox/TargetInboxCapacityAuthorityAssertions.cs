using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using ManagedCode.Communication;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class TargetInboxCapacityAuthorityAssertions
{
    internal static async Task SameAsync(ZoneTreeStore store, string[] image, long position)
    {
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }

    internal static async Task RefuseAsync(DatabaseEngine database, ZoneTreeStore store, CommitInboxRequest request,
        CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        Result<object>? result = null;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            result = TargetInboxSetup.Apply(database, request, token));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(failure.Message).IsEqualTo("The retained target inbox is inconsistent.");
        await Assert.That(result).IsNull();
        await Assert.That(OutcomeStoreOracle.ReadPartition(store, request.Target.Partition,
            TargetInboxUnitProtocol.Root, request.CommandId)).IsNull();
        await SameAsync(store, image, position);
    }

    internal static async Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store, CommitInboxRequest request,
        Result<object> expected, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        await ReplayResultAssertions.Same<CommitInboxResult>(TargetInboxSetup.Apply(database, request, token), expected);
        await SameAsync(store, image, position);
    }

    internal static async Task HistoryAsync(DatabaseEngine database, ZoneTreeStore store,
        TargetInboxCapacityAuthorityState state, CancellationToken token)
    {
        await ReplayAsync(database, store, state.Original, state.OriginalResult, token);
        await ReplayAsync(database, store, state.Second, state.SecondResult, token);
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        await ReplayResultAssertions.Same<CommitReceipt>(QueueWholeFlowStorage.Apply(database, OperationKind.Delivery,
            state.SourceAck, state.SourceAck.CommandId, QueueWholeFlowStorage.Clock(store)), state.SourceAckResult);
        await SameAsync(store, image, position);
    }

    internal static async Task SourceAsync(DatabaseEngine database, TargetInboxCapacityAuthorityState state)
    {
        var source = database.InspectMessage(TargetInboxUnitProtocol.Root, state.Original.Source, state.Original.MessageId)!;
        await Assert.That(source.Metadata.State).IsEqualTo(MessageState.Acked);
        await Assert.That(source.PayloadJson).IsNull();
        await Assert.That(source.HeadersJson).IsNull();
        await TargetInboxAssertions.EffectsAsync(database, state.Original);
    }
}
