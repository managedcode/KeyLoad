namespace KeyLoad.UnitTests.Features.Messaging;

internal static class RemoteTransferAckedRetentionColdTrial
{
    internal const string Message = "acked-retention-original";
    internal const string RefusedMessage = "acked-retention-refused";
    internal const string Payload = "{\"retained\":true}";
    internal const int Ceiling = 1;

    internal static async Task RunAsync()
    {
        using var fixture = new RemoteTransferDatabase(new() { MaxScanRecords = Ceiling });
        var other = new QueueLaneRef(new(RemoteTransferDatabase.TenantId, RemoteTransferDatabase.DatabaseId,
            RemoteTransferDatabase.Domain, RemoteTransferDatabase.OtherSourcePartitionId), RemoteTransferDatabase.OtherSourceQueueName);
        fixture.ConfigureQueue(other);
        var transfer = new CreateQueueTransfer(fixture.SourceQueue, Guid.NewGuid(), fixture.DestinationQueue,
            new(fixture.DestinationQueue.Queue, Message, Payload));
        var second = new CreateQueueTransfer(other, Guid.NewGuid(), fixture.DestinationQueue,
            new(fixture.DestinationQueue.Queue, RefusedMessage, Payload));
        fixture.Commit(fixture.SourcePartition, transfer);
        fixture.Commit(other.Partition, second);
        var original = fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal, fixture.SourceQueue, transfer.TransferId)!;
        var pending = fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal, other, second.TransferId)!;
        var accept = new CommandRequest(Guid.NewGuid(), fixture.DestinationPartition,
            [new AcceptQueueTransfer(fixture.DestinationQueue, original.IntentToken)]);
        var accepted = fixture.Apply(OperationKind.Batch, accept).Get<CommitReceipt>();
        var proof = fixture.Database.InspectQueueTransferReceipt(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, fixture.SourceQueue, transfer.TransferId)!;
        await Assert.That(proof.TargetCommit).IsEqualTo(accepted.Token);
        var received = fixture.Apply(OperationKind.Receive, new ReceiveRequest(Guid.NewGuid(), fixture.DestinationQueue)).Get<ReceiveResult>();
        var delivery = await Assert.That(received.Deliveries).HasSingleItem();
        await Assert.That(delivery.PayloadJson).IsEqualTo(Payload);
        var ack = new DeliveryCommand(Guid.NewGuid(), fixture.DestinationQueue, delivery.Token, DeliveryAction.Ack);
        var acked = fixture.Apply(OperationKind.Delivery, ack, id: ack.CommandId).Get<CommitReceipt>();
        var message = fixture.Database.InspectMessage(RemoteTransferDatabase.RootPrincipal, fixture.DestinationQueue, Message)!;
        await Assert.That(message.Metadata.State).IsEqualTo(MessageState.Acked);
        await Assert.That(message.PayloadJson).IsNull();
        var capacity = fixture.TargetCapacity(fixture.DestinationQueue);
        await Assert.That(capacity.StoredRecords).IsEqualTo((long)Ceiling);
        var refused = new CommandRequest(Guid.NewGuid(), fixture.DestinationPartition,
            [new AcceptQueueTransfer(fixture.DestinationQueue, pending.IntentToken)]);
        var failure = fixture.Apply(OperationKind.Batch, refused);
        await Assert.That(failure.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        var originalBytes = RemoteTransferAttemptColdNative.Outcome(fixture, accept.CommandId, fixture.DestinationPartition);
        var failureBytes = RemoteTransferAttemptColdNative.Outcome(fixture, refused.CommandId, fixture.DestinationPartition);
        fixture.Reopen();
        await RemoteTransferAckedRetentionColdAssertions.RequireAsync(fixture, transfer, second, original, pending,
            proof, message, capacity, accept, accepted, refused, failure, originalBytes, failureBytes);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Apply(OperationKind.Delivery, ack, id: ack.CommandId).Get<CommitReceipt>(), acked);
        var complete = new CommandRequest(Guid.NewGuid(), fixture.SourcePartition,
            [new CompleteQueueTransfer(fixture.SourceQueue, transfer.TransferId, proof.ReceiptToken)]);
        var completed = fixture.Apply(OperationKind.Batch, complete).Get<CommitReceipt>();
        fixture.Reopen();
        await RemoteTransferAckedRetentionColdAssertions.RequireAsync(fixture, transfer, second,
            original with { State = QueueTransferState.Delivered, ReceiptToken = proof.ReceiptToken }, pending,
            proof, message, capacity, accept, accepted, refused, failure, originalBytes, failureBytes);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Apply(OperationKind.Batch, complete).Get<CommitReceipt>(), completed);
    }
}
