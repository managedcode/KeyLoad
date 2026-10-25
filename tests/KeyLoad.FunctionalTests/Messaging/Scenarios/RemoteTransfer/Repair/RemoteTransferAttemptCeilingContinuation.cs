using ManagedCode.Communication;
namespace KeyLoad.FunctionalTests.Messaging;

internal static class RemoteTransferAttemptCeilingContinuation
{
    internal static async Task RunAsync(RemoteTransferDatabase fixture, CreateQueueTransfer transfer,
        QueueTransferInspection original, CommandRequest firstAccept, Result<object> failed, byte[] bytes,
        CommandRequest advance)
    {
        var state = RemoteTransferAttemptCold.State(fixture, transfer.TransferId);
        var refused = fixture.Apply(OperationKind.Batch, advance);
        await Assert.That(Errors.Code(refused.Problem).Value).IsEqualTo(ErrorCode.ResourceExhausted);

        await Assert.That(refused.Value).IsNull();
        var refusedBytes = RemoteTransferAttemptCold.Outcome(fixture, advance.CommandId, fixture.SourcePartition);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(RemoteTransferAttemptCold.State(fixture, transfer.TransferId), state);
        await RemoteTransferAttemptCold.AccountingAsync(fixture, transfer.TransferId, RemoteTransferAttemptColdProtocol.FirstGeneration);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Database.InspectQueueTransfer(
            RemoteTransferDatabase.RootPrincipal, fixture.SourceQueue, transfer.TransferId), original);
        await Assert.That(fixture.Database.InspectQueueTransferReceipt(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, fixture.SourceQueue, transfer.TransferId)).IsNull();
        await Assert.That(fixture.Database.InspectMessage(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, RemoteTransferAttemptColdProtocol.OriginalMessage)).IsNull();
        fixture.Reopen();
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Apply(OperationKind.Batch, advance), refused);
        await Assert.That(RemoteTransferAttemptCold.Outcome(fixture, advance.CommandId,
            fixture.SourcePartition).AsSpan().SequenceEqual(refusedBytes)).IsTrue();
        var acceptedRequest = new CommandRequest(Guid.NewGuid(), fixture.DestinationPartition,
            [new AcceptQueueTransfer(fixture.DestinationQueue, original.IntentToken)]);
        var accepted = fixture.Apply(OperationKind.Batch, acceptedRequest).Get<CommitReceipt>();
        var proof = fixture.Database.InspectQueueTransferReceipt(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, fixture.SourceQueue, transfer.TransferId)!;
        await Assert.That(proof.TargetCommit).IsEqualTo(accepted.Token);
        var complete = new CommandRequest(Guid.NewGuid(), fixture.SourcePartition,
            [new CompleteQueueTransfer(fixture.SourceQueue, transfer.TransferId, proof.ReceiptToken)]);
        var completed = fixture.Apply(OperationKind.Batch, complete).Get<CommitReceipt>();
        var expected = new MessageInspection(new(RemoteTransferAttemptColdProtocol.OriginalMessage, MessageState.Ready,
            RemoteTransferAttemptColdProtocol.InitialAttempts, RemoteTransferAttemptColdProtocol.InitialStateVersion,
            RemoteTransferAttemptColdProtocol.SecondReadySequence, null, null), RemoteTransferAttemptColdProtocol.Payload,
            RemoteTransferAttemptColdProtocol.Headers);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Database.InspectMessage(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, RemoteTransferAttemptColdProtocol.OriginalMessage), expected);
        fixture.Reopen();
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Apply(OperationKind.Batch, acceptedRequest).Get<CommitReceipt>(), accepted);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Apply(OperationKind.Batch, complete).Get<CommitReceipt>(), completed);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Apply(OperationKind.Batch, advance), refused);
        await Assert.That(RemoteTransferAttemptCold.Outcome(fixture, advance.CommandId,
            fixture.SourcePartition).AsSpan().SequenceEqual(refusedBytes)).IsTrue();
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Apply(OperationKind.Batch, firstAccept), failed);
        await Assert.That(RemoteTransferAttemptCold.Outcome(fixture, firstAccept.CommandId,
            fixture.DestinationPartition).AsSpan().SequenceEqual(bytes)).IsTrue();
        await RemoteTransferCoordinationColdAssertions.EqualAsync(RemoteTransferAttemptCold.State(fixture, transfer.TransferId), state);
        await RemoteTransferAttemptCold.AccountingAsync(fixture, transfer.TransferId, RemoteTransferAttemptColdProtocol.FirstGeneration);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            fixture.SourceQueue, transfer.TransferId), original with
            {
                State = QueueTransferState.Delivered,
                ReceiptToken = proof.ReceiptToken
            });
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Database.InspectQueueTransferReceipt(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, fixture.SourceQueue, transfer.TransferId), proof);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Database.InspectMessage(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, RemoteTransferAttemptColdProtocol.OriginalMessage), expected);
    }
}
