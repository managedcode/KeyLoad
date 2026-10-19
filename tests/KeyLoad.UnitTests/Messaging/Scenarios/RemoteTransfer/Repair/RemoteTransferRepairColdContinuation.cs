using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class RemoteTransferRepairColdContinuation
{
    internal static async Task RunAsync(RemoteTransferDatabase fixture, CreateQueueTransfer transfer,
        QueueTransferInspection original, RemoteTransferCoordinationHint hint, CommandRequest deniedAccept,
        byte[] acceptBytes, CommandRequest acceptedCommand, CommitReceipt accepted, int ceiling)
    {
        var proof = fixture.Database.InspectQueueTransferReceipt(RemoteTransferRepairColdProtocol.Subject,
            fixture.DestinationQueue, fixture.SourceQueue, transfer.TransferId)!;
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Database.InspectMessage(RemoteTransferRepairColdProtocol.Subject,
            fixture.DestinationQueue, RemoteTransferRepairColdProtocol.Message), new MessageInspection(new(RemoteTransferRepairColdProtocol.Message,
                MessageState.Ready, RemoteTransferRepairColdProtocol.NoAttempts, RemoteTransferRepairColdProtocol.InitialStateVersion,
                RemoteTransferRepairColdProtocol.InitialReadySequence, null, null), RemoteTransferRepairColdProtocol.Payload, RemoteTransferRepairColdProtocol.Headers));
        RemoteTransferRepairCold.Policy(fixture, fixture.SourceQueue, allow: false);
        var complete = new CommandRequest(RemoteTransferRepairIdentity.CommandId(hint, QueueTransferRepairStage.Complete),
            fixture.SourcePartition, [new CompleteQueueTransfer(fixture.SourceQueue, transfer.TransferId, proof.ReceiptToken)]);
        var failed = RemoteTransferRepairCold.Apply(fixture, complete);
        await Assert.That(Errors.Code(failed.Problem)).IsEqualTo(ErrorCode.PermissionDenied);

        await Assert.That(failed.Value).IsNull();
        var completeBytes = RemoteTransferRepairCold.Outcome(fixture, complete.CommandId, fixture.SourcePartition);
        await RemoteTransferRepairColdAdvance.DeniedWithoutWitnessAsync(fixture, hint, original.IntentToken,
            QueueTransferRepairStage.Complete, proof.ReceiptToken);
        RemoteTransferRepairCold.Policy(fixture, fixture.SourceQueue, allow: true);
        var advance = await RemoteTransferRepairColdAdvance.ApplyAsync(fixture, hint, original.IntentToken,
            QueueTransferRepairStage.Complete, proof.ReceiptToken);
        RemoteTransferRepairColdFailure? refused = null;
        if (ceiling == RemoteTransferRepairColdProtocol.ExhaustedCeiling)
        {
            await Assert.That(Errors.Code(advance.Result.Problem)).IsEqualTo(ErrorCode.ResourceExhausted);
            await Assert.That(advance.Result.Problem?.Detail).IsEqualTo(RemoteTransferRepairProtocol.Capacity);

            await Assert.That(advance.Result.Value).IsNull();
            refused = new(advance.Command, advance.Result,
                RemoteTransferRepairCold.Outcome(fixture, advance.Command.CommandId, fixture.SourcePartition));
            await Assert.That(RemoteTransferRepairCold.State(fixture, transfer.TransferId).CompleteGeneration)
                .IsEqualTo(RemoteTransferRepairColdProtocol.FirstGeneration);
        }
        else
        {
            _ = advance.Result.Get<CommitReceipt>();
        }
        hint = RemoteTransferRepairCold.Hint(fixture);
        var id = ceiling == RemoteTransferRepairColdProtocol.ExhaustedCeiling ? Guid.NewGuid()
            : RemoteTransferRepairIdentity.CommandId(hint, QueueTransferRepairStage.Complete);
        var repaired = new CommandRequest(id, fixture.SourcePartition, complete.Mutations);
        var delivered = RemoteTransferRepairCold.Apply(fixture, repaired).Get<CommitReceipt>();
        await RemoteTransferRepairColdHealthy.ColdAsync(fixture, transfer, original, proof, deniedAccept, acceptBytes,
            complete, completeBytes, acceptedCommand, accepted, repaired, delivered, ceiling, refused);
    }
}
