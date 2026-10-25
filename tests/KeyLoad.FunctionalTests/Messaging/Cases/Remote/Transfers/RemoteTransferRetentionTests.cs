using KeyLoad.Core.Messaging;

namespace KeyLoad.FunctionalTests.Messaging;

internal sealed class RemoteTransferRetentionTests
{
    private const string Payload = "{\"retained\":true}";
    private const string FirstId = "transfer-one";
    private const string SourceMessageId = "disposable-source-message";

    [Test]
    public async Task SourceIntentOwnsItsPayloadAfterTheOriginalQueueBodyIsAcknowledged()
    {
        using var fixture = new RemoteTransferDatabase();
        fixture.Commit(fixture.SourcePartition, new EnqueueMessage(RemoteTransferDatabase.SourceQueueName,
            SourceMessageId, Payload));
        var transfer = Create(fixture, fixture.SourceQueue, FirstId);
        fixture.Commit(fixture.SourcePartition, transfer);
        var sourceDelivery = await Assert.That(fixture.Apply(OperationKind.Receive,
            new ReceiveRequest(Guid.NewGuid(), fixture.SourceQueue)).Get<ReceiveResult>().Deliveries).HasSingleItem();
        fixture.Apply(OperationKind.Delivery, new DeliveryCommand(Guid.NewGuid(), fixture.SourceQueue,
            sourceDelivery.Token, DeliveryAction.Ack)).Get<CommitReceipt>();

        await Assert.That(fixture.Database.InspectMessage(RemoteTransferDatabase.RootPrincipal,
            fixture.SourceQueue, SourceMessageId)!.PayloadJson).IsNull();
        var retained = fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            fixture.SourceQueue, transfer.TransferId)!;
        var intent = fixture.Database.Verify<RemoteTransferIntentClaims>(retained.IntentToken);
        await Assert.That(intent.Message.PayloadJson).IsEqualTo(Payload);
        await Assert.That(retained.State).IsEqualTo(QueueTransferState.OutputPending);
    }

    private static CreateQueueTransfer Create(RemoteTransferDatabase fixture, QueueLaneRef source, string messageId)
        => new(source, Guid.NewGuid(), fixture.DestinationQueue,
            new(fixture.DestinationQueue.Queue, messageId, Payload));

}
