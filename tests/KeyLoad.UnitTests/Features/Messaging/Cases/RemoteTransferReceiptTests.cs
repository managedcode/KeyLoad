using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class RemoteTransferReceiptTests
{
    private const string Principal = "second-root";
    private const string Payload = "{\"receipt\":true}";
    private const string ChangedFingerprint = "changed-fingerprint";
    private const string ChangedPurpose = "keyload.queue-transfer.receipt.invalid";
    private const string OtherSourcePartitionId = "other-source-partition";
    private const string OtherTargetPartitionId = "other-target-partition";

    [Test]
    public async Task OnlyTheMatchingSignedCommittedReceiptCanCompleteAndRetryTheSource()
    {
        using var fixture = new RemoteTransferDatabase();
        fixture.AddPrincipal(new(Principal, RemoteTransferDatabase.TenantId,
            [new("database", RemoteTransferDatabase.SourceQueueName, Capability.QueuePublish)], [])
        { ClusterAdministrator = true });
        var transferId = Guid.NewGuid();
        fixture.Commit(fixture.SourcePartition, new CreateQueueTransfer(fixture.SourceQueue, transferId,
            fixture.DestinationQueue, new(fixture.DestinationQueue.Queue, "message-1", Payload)));
        var pending = fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            fixture.SourceQueue, transferId)!;
        var intent = fixture.Database.Verify<RemoteTransferIntentClaims>(pending.IntentToken);
        fixture.Commit(fixture.DestinationPartition, new AcceptQueueTransfer(fixture.DestinationQueue, pending.IntentToken));
        var target = fixture.Database.InspectQueueTransferReceipt(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, fixture.SourceQueue, transferId)!;
        var receipt = fixture.Database.Verify<RemoteTransferReceiptClaims>(target.ReceiptToken);

        foreach (var invalidToken in InvalidReceipts(fixture, receipt, target.ReceiptToken))
        {
            var rejected = fixture.Apply(OperationKind.Batch,
                new CommandRequest(Guid.NewGuid(), fixture.SourcePartition,
                    [new CompleteQueueTransfer(fixture.SourceQueue, transferId, invalidToken)]));
            await Assert.That(rejected.Error).IsEqualTo(ErrorCode.TokenInvalidated);
            await Assert.That(fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
                fixture.SourceQueue, transferId)!.State).IsEqualTo(QueueTransferState.OutputPending);
        }

        var wrongPrincipal = fixture.Apply(OperationKind.Batch,
            new CommandRequest(Guid.NewGuid(), fixture.SourcePartition,
                [new CompleteQueueTransfer(fixture.SourceQueue, transferId, target.ReceiptToken)]), Principal);
        await Assert.That(wrongPrincipal.Error).IsEqualTo(ErrorCode.TokenInvalidated);
        var completed = fixture.Commit(fixture.SourcePartition,
            new CompleteQueueTransfer(fixture.SourceQueue, transferId, target.ReceiptToken));
        var delivered = fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            fixture.SourceQueue, transferId)!;
        await Assert.That(delivered.State).IsEqualTo(QueueTransferState.Delivered);
        await Assert.That(delivered.IntentToken).IsEqualTo(pending.IntentToken);
        await Assert.That(delivered.ReceiptToken).IsEqualTo(target.ReceiptToken);
        await Assert.That(completed.Mutations[0].Id).IsEqualTo(transferId.ToString("N"));
        var replay = fixture.Commit(fixture.SourcePartition,
            new CompleteQueueTransfer(fixture.SourceQueue, transferId, target.ReceiptToken));
        await Assert.That(replay.Mutations[0].Revision).IsEqualTo(completed.Mutations[0].Revision);
        await Assert.That(fixture.Database.Verify<RemoteTransferIntentClaims>(delivered.IntentToken)).IsEqualTo(intent);
        await Assert.That(fixture.Database.InspectQueueTransferReceipt(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, fixture.SourceQueue, transferId)!.TargetCommit).IsEqualTo(target.TargetCommit);
    }

    private static string[] InvalidReceipts(RemoteTransferDatabase fixture, RemoteTransferReceiptClaims receipt,
        string validToken)
    {
        var otherSource = new QueueLaneRef(new(RemoteTransferDatabase.TenantId,
            RemoteTransferDatabase.DatabaseId, "orders", OtherSourcePartitionId), "other-source");
        var otherTarget = new QueueLaneRef(new(RemoteTransferDatabase.TenantId,
            RemoteTransferDatabase.DatabaseId, "orders", OtherTargetPartitionId), "other-target");
        return
        [
            fixture.Database.Sign(receipt with { Purpose = ChangedPurpose }),
            fixture.Database.Sign(receipt with { Incarnation = Guid.NewGuid() }),
            fixture.Database.Sign(receipt with { Source = otherSource }),
            fixture.Database.Sign(receipt with { TransferId = Guid.NewGuid() }),
            fixture.Database.Sign(receipt with { Destination = otherTarget }),
            fixture.Database.Sign(receipt with { PrincipalId = Principal }),
            fixture.Database.Sign(receipt with { Fingerprint = ChangedFingerprint }),
            fixture.Database.Sign(receipt with { TargetCommit = receipt.TargetCommit with { Position = receipt.TargetCommit.Position + 1 } }),
            validToken[..^1] + "!"
        ];
    }
}
