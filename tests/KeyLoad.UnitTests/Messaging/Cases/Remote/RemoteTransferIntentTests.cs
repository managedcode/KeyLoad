
namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class RemoteTransferIntentTests
{
    private const string IntentPayload = "{\"order\":\"A-104\"}";
    private const string ChangedPayload = "{\"order\":\"A-105\"}";
    private const string AlternateQueue = "destination-alternate";

    [Test]
    public async Task SameIdentityRetainsOriginalIntentAndChangedBodyOrDestinationConflicts()
    {
        using var fixture = new RemoteTransferDatabase();
        var alternate = new QueueLaneRef(fixture.DestinationPartition, AlternateQueue);
        fixture.ConfigureQueue(alternate);
        var transferId = Guid.NewGuid();
        var message = new EnqueueMessage(fixture.DestinationQueue.Queue, "transfer-1", IntentPayload);
        var create = new CreateQueueTransfer(fixture.SourceQueue, transferId, fixture.DestinationQueue, message);
        fixture.Commit(fixture.SourcePartition, create);

        var before = fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal, fixture.SourceQueue, transferId);
        await Assert.That(before).IsNotNull();
        await Assert.That(before!.State).IsEqualTo(QueueTransferState.OutputPending);
        var replay = fixture.Commit(fixture.SourcePartition, create);
        var after = fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal, fixture.SourceQueue, transferId);
        await Assert.That(replay.Mutations[0].Id).IsEqualTo(transferId.ToString("N"));
        await Assert.That(after!.IntentToken).IsEqualTo(before.IntentToken);
        await Assert.That(after.State).IsEqualTo(QueueTransferState.OutputPending);

        var changedBody = fixture.Apply(OperationKind.Batch,
            new CommandRequest(Guid.NewGuid(), fixture.SourcePartition,
                [create with { Message = message with { PayloadJson = ChangedPayload } }])).Error;
        await Assert.That(changedBody).IsEqualTo(ErrorCode.Conflict);
        var changedDestination = fixture.Apply(OperationKind.Batch,
            new CommandRequest(Guid.NewGuid(), fixture.SourcePartition,
                [create with { Destination = alternate, Message = message with { Queue = alternate.Queue } }])).Error;
        await Assert.That(changedDestination).IsEqualTo(ErrorCode.Conflict);
        var preserved = fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal, fixture.SourceQueue, transferId);
        await Assert.That(preserved!.Destination).IsEqualTo(fixture.DestinationQueue);
        await Assert.That(preserved.IntentToken).IsEqualTo(before.IntentToken);
    }

    [Test]
    public async Task LaterBatchFailureRollsBackIntentAndPendingIntentSurvivesStoreReopen()
    {
        using var fixture = new RemoteTransferDatabase();
        fixture.Commit(fixture.SourcePartition, new EnqueueMessage(RemoteTransferDatabase.SourceQueueName, "occupied", "{}"));
        var rolledBackId = Guid.NewGuid();
        var rollback = fixture.Apply(OperationKind.Batch, new CommandRequest(Guid.NewGuid(), fixture.SourcePartition,
        [
            new CreateQueueTransfer(fixture.SourceQueue, rolledBackId, fixture.DestinationQueue,
                new(fixture.DestinationQueue.Queue, "rolled-back", "{}")),
            new EnqueueMessage(RemoteTransferDatabase.SourceQueueName, "occupied", "{}")
        ]));
        await Assert.That(rollback.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            fixture.SourceQueue, rolledBackId)).IsNull();

        var transferId = Guid.NewGuid();
        fixture.Commit(fixture.SourcePartition, new CreateQueueTransfer(fixture.SourceQueue, transferId,
            fixture.DestinationQueue, new(fixture.DestinationQueue.Queue, "retained", "{\"body\":7}")));
        var before = fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal, fixture.SourceQueue, transferId);
        fixture.Reopen();
        var after = fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal, fixture.SourceQueue, transferId);
        await Assert.That(after).IsNotNull();
        await Assert.That(after!.State).IsEqualTo(QueueTransferState.OutputPending);
        await Assert.That(after.IntentToken).IsEqualTo(before!.IntentToken);
        await Assert.That(fixture.Database.InspectQueueTransferReceipt(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, fixture.SourceQueue, transferId)).IsNull();
    }
}
