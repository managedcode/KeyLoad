using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class RemoteTransferAttemptColdContinuation
{
    internal static async Task RunAsync(RemoteTransferDatabase fixture, CreateQueueTransfer transfer,
        QueueTransferInspection original, RemoteTransferCoordinationHint hint, CommandRequest firstAccept,
        OperationResult failed, byte[] failureBytes, CommandRequest advance, CommitReceipt advanced,
        RemoteTransferAcceptFailureClaims claims)
    {
        fixture.Reopen();
        var state = RemoteTransferAttemptColdNative.State(fixture, transfer.TransferId);
        await Assert.That(state.Generation).IsEqualTo(RemoteTransferAttemptColdProtocol.SecondGeneration);
        await Assert.That(fixture.SourceCapacity(fixture.SourceQueue).StoredRecords).IsEqualTo(RemoteTransferAttemptColdProtocol.TwoRetainedRecords);
        await Assert.That(state.Ceiling).IsEqualTo(RemoteTransferAttemptColdProtocol.Ceiling);
        var reference = await Assert.That(state.History).HasSingleItem();
        await Assert.That(reference.Generation).IsEqualTo(RemoteTransferAttemptColdProtocol.FirstGeneration);
        await Assert.That(reference.WitnessToken).IsEqualTo(((AdvanceQueueTransferAttempt)advance.Mutations.Single()).FailureWitness);
        await RemoteTransferAttemptColdNative.AccountingAsync(fixture, transfer.TransferId, RemoteTransferAttemptColdProtocol.TwoRetainedRecords);
        await Assert.That(reference.AcceptCommandId).IsEqualTo(firstAccept.CommandId);
        await Assert.That(reference.OutcomeDigest).IsEqualTo(claims.OutcomeDigest);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Apply(OperationKind.Batch, advance).Get<CommitReceipt>(), advanced);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Apply(OperationKind.Batch, firstAccept), failed);
        await Assert.That(RemoteTransferAttemptColdNative.Outcome(fixture, firstAccept.CommandId,
            fixture.DestinationPartition).AsSpan().SequenceEqual(failureBytes)).IsTrue();
        var acceptedRequest = new CommandRequest(RemoteTransferAttemptIdentity.AcceptId(hint, state.Generation),
            fixture.DestinationPartition, [new AcceptQueueTransfer(fixture.DestinationQueue, original.IntentToken)]);
        var accepted = fixture.Apply(OperationKind.Batch, acceptedRequest).Get<CommitReceipt>();
        var receipt = fixture.Database.InspectQueueTransferReceipt(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, fixture.SourceQueue, transfer.TransferId)!;
        await Assert.That(receipt.TargetCommit).IsEqualTo(accepted.Token);
        var complete = new CommandRequest(RemoteTransferCoordinationIdentity.CommandId(hint, RemoteTransferCoordinationProtocol.CompleteStage),
            fixture.SourcePartition, [new CompleteQueueTransfer(fixture.SourceQueue, transfer.TransferId, receipt.ReceiptToken)]);
        var completed = fixture.Apply(OperationKind.Batch, complete).Get<CommitReceipt>();
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Database.InspectMessage(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, RemoteTransferAttemptColdProtocol.OriginalMessage),
            new MessageInspection(new(RemoteTransferAttemptColdProtocol.OriginalMessage, MessageState.Ready,
                RemoteTransferAttemptColdProtocol.InitialAttempts, RemoteTransferAttemptColdProtocol.InitialStateVersion,
                RemoteTransferAttemptColdProtocol.SecondReadySequence, null, null), RemoteTransferAttemptColdProtocol.Payload, RemoteTransferAttemptColdProtocol.Headers));
        await RemoteTransferAttemptColdNative.AckAsync(fixture, RemoteTransferAttemptColdProtocol.OriginalMessage);
        var acked = fixture.Database.InspectMessage(RemoteTransferDatabase.RootPrincipal, fixture.DestinationQueue,
            RemoteTransferAttemptColdProtocol.OriginalMessage)!;
        await Assert.That(acked.Metadata.State).IsEqualTo(MessageState.Acked);
        await Assert.That(RemoteTransferAttemptColdNative.Read(fixture, RemoteTransferAttemptColdNative.Request(fixture,
            transfer, original.IntentToken, firstAccept.CommandId, RemoteTransferAttemptProtocol.FailureReadPurpose)).TargetReceipt)
            .IsEqualTo(receipt);
        fixture.Reopen();
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Apply(OperationKind.Batch, acceptedRequest).Get<CommitReceipt>(), accepted);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Apply(OperationKind.Batch, complete).Get<CommitReceipt>(), completed);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Database.InspectMessage(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, RemoteTransferAttemptColdProtocol.OriginalMessage), acked);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(RemoteTransferAttemptColdNative.State(fixture, transfer.TransferId), state);
        await RemoteTransferAttemptColdNative.AccountingAsync(fixture, transfer.TransferId, RemoteTransferAttemptColdProtocol.TwoRetainedRecords);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            fixture.SourceQueue, transfer.TransferId), original with { State = QueueTransferState.Delivered, ReceiptToken = receipt.ReceiptToken });
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Database.InspectQueueTransferReceipt(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, fixture.SourceQueue, transfer.TransferId), receipt);
        await Assert.That(RemoteTransferAttemptColdNative.Outcome(fixture, firstAccept.CommandId,
            fixture.DestinationPartition).AsSpan().SequenceEqual(failureBytes)).IsTrue();
    }
}
