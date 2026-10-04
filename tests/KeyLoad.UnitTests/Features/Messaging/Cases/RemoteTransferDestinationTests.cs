using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class RemoteTransferDestinationTests
{
    private const string Payload = "{\"derived\":true}";
    private const string HeaderJson = "{\"origin\":\"source\"}";

    [Test]
    public async Task DestinationEnqueuesOnceAndRetainsReceiptAfterMessageAcknowledgement()
    {
        using var fixture = new RemoteTransferDatabase();
        var transferId = Guid.NewGuid();
        var intent = new CreateQueueTransfer(fixture.SourceQueue, transferId, fixture.DestinationQueue,
            new(fixture.DestinationQueue.Queue, "remote-1", Payload, HeaderJson));
        fixture.Commit(fixture.SourcePartition, intent);
        var source = fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal, fixture.SourceQueue, transferId)!;

        fixture.Commit(fixture.DestinationPartition, new AcceptQueueTransfer(fixture.DestinationQueue, source.IntentToken));
        var accepted = fixture.Database.InspectQueueTransferReceipt(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, fixture.SourceQueue, transferId);
        await Assert.That(accepted).IsNotNull();
        var committed = accepted!;
        await Assert.That(committed.TargetCommit.AtomicPartitionId).IsEqualTo(fixture.DestinationPartition.AtomicPartitionId);
        await Assert.That(committed.TargetCommit.Position).IsGreaterThan(0);
        await Assert.That(fixture.Database.InspectMessage(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, "remote-1")!.PayloadJson).IsEqualTo(Payload);
        await Assert.That(fixture.Database.InspectMessage(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, "remote-1")!.HeadersJson).IsEqualTo(HeaderJson);

        var receiveId = Guid.NewGuid();
        var delivery = await Assert.That(fixture.Apply(OperationKind.Receive,
            new ReceiveRequest(receiveId, fixture.DestinationQueue), id: receiveId).Get<ReceiveResult>().Deliveries).HasSingleItem();
        var ackId = Guid.NewGuid();
        fixture.Apply(OperationKind.Delivery,
            new DeliveryCommand(ackId, fixture.DestinationQueue, delivery.Token, DeliveryAction.Ack), id: ackId).Get<CommitReceipt>();
        await Assert.That(fixture.Database.InspectMessage(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, "remote-1")!.Metadata.State).IsEqualTo(MessageState.Acked);

        var replay = fixture.Commit(fixture.DestinationPartition,
            new AcceptQueueTransfer(fixture.DestinationQueue, source.IntentToken));
        var retained = fixture.Database.InspectQueueTransferReceipt(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, fixture.SourceQueue, transferId);
        await Assert.That(replay.Mutations[0].Revision).IsEqualTo(committed.TargetCommit.Position);
        await Assert.That(retained!.ReceiptToken).IsEqualTo(committed.ReceiptToken);
        await Assert.That(retained.TargetCommit).IsEqualTo(committed.TargetCommit);
        await Assert.That(fixture.Database.InspectMessage(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, "remote-1")!.Metadata.State).IsEqualTo(MessageState.Acked);
    }

    [Test]
    public async Task PersistedDestinationPermissionFailureLeavesNoEffectOrReceipt()
    {
        using var fixture = new RemoteTransferDatabase();
        var transferId = Guid.NewGuid();
        fixture.Commit(fixture.SourcePartition, new CreateQueueTransfer(fixture.SourceQueue, transferId,
            fixture.DestinationQueue, new(fixture.DestinationQueue.Queue, "denied", Payload)));
        var intent = fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal, fixture.SourceQueue, transferId)!;
        fixture.AddPrincipal(new(RemoteTransferDatabase.RootPrincipal, RemoteTransferDatabase.TenantId,
            [new("database", RemoteTransferDatabase.SourceQueueName, Capability.QueuePublish),
             new("database", RemoteTransferDatabase.SourceQueueName, Capability.QueueInspect),
             new("database", RemoteTransferDatabase.DestinationQueueName, Capability.QueueInspect)], ["*"])
        { ClusterAdministrator = true, PolicyEpoch = 2 });

        var rejected = fixture.Apply(OperationKind.Batch,
            new CommandRequest(Guid.NewGuid(), fixture.DestinationPartition,
                [new AcceptQueueTransfer(fixture.DestinationQueue, intent.IntentToken)]));
        await Assert.That(rejected.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(fixture.Database.InspectQueueTransferReceipt(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, fixture.SourceQueue, transferId)).IsNull();
        await Assert.That(fixture.Database.InspectMessage(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, "denied")).IsNull();
        await Assert.That(fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            fixture.SourceQueue, transferId)!.State).IsEqualTo(QueueTransferState.OutputPending);
    }

    [Test]
    public async Task QueueQuotaFailureRollsBackBothDestinationEffectAndDedupReceipt()
    {
        using var fixture = new RemoteTransferDatabase(destinationPolicy: new() { MaxStoredMessages = 1 });
        fixture.Commit(fixture.DestinationPartition,
            new EnqueueMessage(fixture.DestinationQueue.Queue, "already-present", "{}"));
        var transferId = Guid.NewGuid();
        fixture.Commit(fixture.SourcePartition, new CreateQueueTransfer(fixture.SourceQueue, transferId,
            fixture.DestinationQueue, new(fixture.DestinationQueue.Queue, "quota-failed", Payload)));
        var intent = fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal, fixture.SourceQueue, transferId)!;

        var rejected = fixture.Apply(OperationKind.Batch,
            new CommandRequest(Guid.NewGuid(), fixture.DestinationPartition,
                [new AcceptQueueTransfer(fixture.DestinationQueue, intent.IntentToken)]));
        await Assert.That(rejected.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(fixture.Database.InspectQueueTransferReceipt(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, fixture.SourceQueue, transferId)).IsNull();
        await Assert.That(fixture.Database.InspectMessage(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, "quota-failed")).IsNull();
        await Assert.That(fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            fixture.SourceQueue, transferId)!.State).IsEqualTo(QueueTransferState.OutputPending);
    }

    [Test]
    public async Task ExistingDestinationMessageConflictPreservesTheOriginalBodyAndPendingIntent()
    {
        using var fixture = new RemoteTransferDatabase();
        fixture.Commit(fixture.DestinationPartition,
            new EnqueueMessage(fixture.DestinationQueue.Queue, "occupied-transfer-id", "{\"original\":true}"));
        var transferId = Guid.NewGuid();
        fixture.Commit(fixture.SourcePartition, new CreateQueueTransfer(fixture.SourceQueue, transferId,
            fixture.DestinationQueue, new(fixture.DestinationQueue.Queue, "occupied-transfer-id", Payload)));
        var intent = fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            fixture.SourceQueue, transferId)!;

        var rejected = fixture.Apply(OperationKind.Batch,
            new CommandRequest(Guid.NewGuid(), fixture.DestinationPartition,
                [new AcceptQueueTransfer(fixture.DestinationQueue, intent.IntentToken)]));

        await Assert.That(rejected.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(fixture.Database.InspectQueueTransferReceipt(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, fixture.SourceQueue, transferId)).IsNull();
        await Assert.That(fixture.Database.InspectMessage(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, "occupied-transfer-id")!.PayloadJson).IsEqualTo("{\"original\":true}");
        await Assert.That(fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            fixture.SourceQueue, transferId)!.State).IsEqualTo(QueueTransferState.OutputPending);
    }
}
