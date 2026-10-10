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
        RemoteTransferRepairColdNative.Policy(fixture, fixture.SourceQueue, allow: false);
        var complete = new CommandRequest(RemoteTransferRepairIdentity.CommandId(hint, QueueTransferRepairStage.Complete),
            fixture.SourcePartition, [new CompleteQueueTransfer(fixture.SourceQueue, transfer.TransferId, proof.ReceiptToken)]);
        var failed = RemoteTransferRepairColdNative.Apply(fixture, complete);
        await Assert.That(failed.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(failed.Json).IsNull();
        await Assert.That(failed.NativeValue).IsNull();
        var completeBytes = RemoteTransferRepairColdNative.Outcome(fixture, complete.CommandId, fixture.SourcePartition);
        await RemoteTransferRepairColdAdvance.DeniedWithoutWitnessAsync(fixture, hint, original.IntentToken,
            QueueTransferRepairStage.Complete, proof.ReceiptToken);
        RemoteTransferRepairColdNative.Policy(fixture, fixture.SourceQueue, allow: true);
        var advance = await RemoteTransferRepairColdAdvance.ApplyAsync(fixture, hint, original.IntentToken,
            QueueTransferRepairStage.Complete, proof.ReceiptToken);
        RemoteTransferRepairColdFailure? refused = null;
        if (ceiling == RemoteTransferRepairColdProtocol.ExhaustedCeiling)
        {
            await Assert.That(advance.Result.Error).IsEqualTo(ErrorCode.ResourceExhausted);
            await Assert.That(advance.Result.SafeDetail).IsEqualTo(RemoteTransferRepairProtocol.Capacity);
            await Assert.That(advance.Result.Json).IsNull();
            await Assert.That(advance.Result.NativeValue).IsNull();
            refused = new(advance.Command, advance.Result,
                RemoteTransferRepairColdNative.Outcome(fixture, advance.Command.CommandId, fixture.SourcePartition));
            await Assert.That(RemoteTransferRepairColdNative.State(fixture, transfer.TransferId).CompleteGeneration)
                .IsEqualTo(RemoteTransferRepairColdProtocol.FirstGeneration);
        }
        else
        { _ = advance.Result.Get<CommitReceipt>(); }
        hint = RemoteTransferRepairColdNative.Hint(fixture);
        var id = ceiling == RemoteTransferRepairColdProtocol.ExhaustedCeiling ? Guid.NewGuid()
            : RemoteTransferRepairIdentity.CommandId(hint, QueueTransferRepairStage.Complete);
        var repaired = new CommandRequest(id, fixture.SourcePartition, complete.Mutations);
        var delivered = RemoteTransferRepairColdNative.Apply(fixture, repaired).Get<CommitReceipt>();
        await RemoteTransferRepairColdHealthy.ColdAsync(fixture, transfer, original, proof, deniedAccept, acceptBytes,
            complete, completeBytes, acceptedCommand, accepted, repaired, delivered, ceiling, refused);
    }
}
