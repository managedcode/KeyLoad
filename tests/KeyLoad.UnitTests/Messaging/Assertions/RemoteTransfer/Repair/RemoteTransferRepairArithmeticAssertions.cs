using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class RemoteTransferRepairArithmeticAssertions
{
    private const string HealthyMessage = "repair-arithmetic-healthy";
    private const string CompleteKind = "completeQueueTransfer";
    private const string TransferIdFormat = "N";

    internal static async Task HealthyAsync(RemoteTransferDatabase fixture)
    {
        fixture.Commit(fixture.DestinationPartition, new EnqueueMessage(fixture.DestinationQueue.Queue, HealthyMessage,
            RemoteTransferRepairColdProtocol.Payload));
        var request = new ReceiveRequest(Guid.NewGuid(), fixture.DestinationQueue);
        var claim = fixture.Apply(OperationKind.Receive, request, id: request.RequestId).Get<ReceiveResult>();
        var delivery = await Assert.That(claim.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(HealthyMessage);
        await Assert.That(delivery.PayloadJson).IsEqualTo(RemoteTransferRepairColdProtocol.Payload);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Apply(OperationKind.Receive, request,
            id: request.RequestId).Get<ReceiveResult>(), claim);
        var ack = new DeliveryCommand(Guid.NewGuid(), fixture.DestinationQueue, delivery.Token, DeliveryAction.Ack);
        var receipt = fixture.Apply(OperationKind.Delivery, ack, id: ack.CommandId).Get<CommitReceipt>();
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Apply(OperationKind.Delivery, ack,
            id: ack.CommandId).Get<CommitReceipt>(), receipt);
        await Assert.That(fixture.Database.InspectMessage(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, HealthyMessage)!.Metadata.State).IsEqualTo(MessageState.Acked);
    }

    internal static async Task RefuseAsync(RemoteTransferDatabase fixture, RemoteTransferRepairArithmeticOriginal original,
        string[] image, long position, byte[] identity)
    {
        var read = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Database.InspectQueueTransfer(
            RemoteTransferRepairColdProtocol.Subject, fixture.SourceQueue, original.Transfer.TransferId));
        await Assert.That(read.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(read.Message).IsEqualTo(RemoteTransferRepairProtocol.Invalid);
        var command = Assert.ThrowsExactly<KeyLoadException>(() => RemoteTransferRepairColdNative.Apply(fixture, original.Complete));
        await Assert.That(command.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(command.Message).IsEqualTo(RemoteTransferRepairProtocol.Invalid);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync((ZoneTreeStore)fixture.Store, image, position, identity);
        await OriginalsAsync(fixture, original);
    }

    internal static async Task RestoredAsync(RemoteTransferDatabase fixture, RemoteTransferRepairArithmeticOriginal original,
        bool advancedHistory)
    {
        var bytes = fixture.Store.Read(view => view.ReadOwnedValue(original.IntentKey))
            ?? throw new InvalidOperationException(RemoteTransferRepairColdProtocol.Missing);
        await Assert.That(bytes.AsSpan().SequenceEqual(original.IntentBytes)).IsTrue();
        await Assert.That(QueueWholeFlowStorage.Bytes((ZoneTreeStore)fixture.Store)).IsEquivalentTo(original.OriginalImage, CollectionOrdering.Matching);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Database.InspectQueueTransfer(
            RemoteTransferRepairColdProtocol.Subject, fixture.SourceQueue, original.Transfer.TransferId), original.Intent);
        var state = RemoteTransferRepairColdNative.State(fixture, original.Transfer.TransferId);
        await Assert.That(state.AcceptPolicyGeneration).IsEqualTo(advancedHistory ? 2L : 1L);
        await Assert.That(state.CompleteGeneration).IsEqualTo(1L);
        await Assert.That(state.History.Length).IsEqualTo(advancedHistory ? 1 : 0);
        await OriginalsAsync(fixture, original);
    }

    internal static async Task CompletedAsync(RemoteTransferDatabase fixture, RemoteTransferRepairArithmeticOriginal original,
        CommitReceipt receipt)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(original.Complete.CommandId);
        await Assert.That(receipt.Mutations).IsEquivalentTo(new MutationReceipt[] {
            new(CompleteKind, fixture.SourceQueue.Queue, original.Transfer.TransferId.ToString(TransferIdFormat), 2L) }, CollectionOrdering.Matching);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(RemoteTransferRepairColdNative.Apply(fixture,
            original.Complete).Get<CommitReceipt>(), receipt);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Database.InspectQueueTransfer(
            RemoteTransferRepairColdProtocol.Subject, fixture.SourceQueue, original.Transfer.TransferId),
            original.Intent with
            {
                State = QueueTransferState.Delivered,
                ReceiptToken = original.Proof.ReceiptToken
            });
        await OriginalsAsync(fixture, original);
    }

    internal static async Task SettledAsync(RemoteTransferDatabase fixture, RemoteTransferRepairArithmeticOriginal original,
        CommitReceipt completed, bool advancedHistory)
    {
        var cut = QueueWholeFlowStorage.Bytes((ZoneTreeStore)fixture.Store);
        var position = fixture.Store.Position;
        var identity = NativeSerialization.Serialize(fixture.Store.Identity);
        await CompletedAsync(fixture, original, completed);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(RemoteTransferRepairColdNative.Apply(fixture,
            original.Accepted).Get<CommitReceipt>(), original.Acceptance);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Database.InspectQueueTransferReceipt(
            RemoteTransferRepairColdProtocol.Subject, fixture.DestinationQueue, fixture.SourceQueue,
            original.Transfer.TransferId), original.Proof);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Database.InspectMessage(RemoteTransferRepairColdProtocol.Subject,
            fixture.DestinationQueue, RemoteTransferRepairColdProtocol.Message), new MessageInspection(
                new(RemoteTransferRepairColdProtocol.Message, MessageState.Acked, 1, 3L, 1L, null, null, LeaseVersion: 1L), null, null));
        await RemoteTransferAttemptColdNative.AccountingAsync(fixture, original.Transfer.TransferId, advancedHistory ? 2L : 1L);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync((ZoneTreeStore)fixture.Store, cut, position, identity);
    }

    private static async Task OriginalsAsync(RemoteTransferDatabase fixture, RemoteTransferRepairArithmeticOriginal original)
    {
        await Assert.That(RemoteTransferRepairColdNative.Outcome(fixture, original.Create.CommandId,
            fixture.SourcePartition).AsSpan().SequenceEqual(original.CreateBytes)).IsTrue();
        await RemoteTransferCoordinationColdAssertions.EqualAsync(NativeSerialization.Deserialize<StoredOutcome>(
            original.CreateBytes).Result.Get<CommitReceipt>(), original.Created);
        await Assert.That(RemoteTransferRepairColdNative.Outcome(fixture, original.Denied.CommandId,
            fixture.DestinationPartition).AsSpan().SequenceEqual(original.DenialBytes)).IsTrue();
        await RemoteTransferCoordinationColdAssertions.EqualAsync(NativeSerialization.Deserialize<StoredOutcome>(original.DenialBytes).Result, original.Denial);
        var replay = RemoteTransferRepairColdNative.Apply(fixture, original.Denied);
        await Assert.That(Errors.Code(replay.Problem)).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(replay.Problem?.Detail).IsEqualTo("The principal policy changed since this command was evaluated.");

        await Assert.That(replay.Value).IsNull();
    }
}
