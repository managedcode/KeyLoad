using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class RemoteTransferRepairColdHealthy
{
    internal static async Task ColdAsync(RemoteTransferDatabase fixture, CreateQueueTransfer transfer,
        QueueTransferInspection original, QueueTransferReceiptInspection proof, CommandRequest deniedAccept,
        byte[] acceptBytes, CommandRequest deniedComplete, byte[] completeBytes, CommandRequest acceptedCommand,
        CommitReceipt accepted, CommandRequest completedCommand, CommitReceipt completed, int ceiling, RemoteTransferRepairColdFailure? refused)
    {
        await RemoteTransferAttemptColdNative.AckAsync(fixture, RemoteTransferRepairColdProtocol.Message);
        await ProveAsync(fixture, transfer, original, proof, deniedAccept, acceptBytes, deniedComplete,
            completeBytes, acceptedCommand, accepted, completedCommand, completed, ceiling, refused);
        fixture.Reopen();
        await ProveAsync(fixture, transfer, original, proof, deniedAccept, acceptBytes, deniedComplete,
            completeBytes, acceptedCommand, accepted, completedCommand, completed, ceiling, refused);
        fixture.Reopen();
        await ProveAsync(fixture, transfer, original, proof, deniedAccept, acceptBytes, deniedComplete,
            completeBytes, acceptedCommand, accepted, completedCommand, completed, ceiling, refused);
    }

    private static async Task ProveAsync(RemoteTransferDatabase fixture, CreateQueueTransfer transfer,
        QueueTransferInspection original, QueueTransferReceiptInspection proof, CommandRequest deniedAccept,
        byte[] acceptBytes, CommandRequest deniedComplete, byte[] completeBytes, CommandRequest acceptedCommand,
        CommitReceipt accepted, CommandRequest completedCommand, CommitReceipt completed, int ceiling, RemoteTransferRepairColdFailure? refused)
    {
        await Assert.That(RemoteTransferRepairColdNative.Outcome(fixture, deniedAccept.CommandId, fixture.DestinationPartition).AsSpan().SequenceEqual(acceptBytes)).IsTrue();
        await Assert.That(RemoteTransferRepairColdNative.Outcome(fixture, deniedComplete.CommandId, fixture.SourcePartition).AsSpan().SequenceEqual(completeBytes)).IsTrue();
        await Assert.That(RemoteTransferRepairColdNative.Apply(fixture, deniedAccept).Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(RemoteTransferRepairColdNative.Apply(fixture, deniedComplete).Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(RemoteTransferRepairColdNative.Apply(fixture, acceptedCommand).Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(RemoteTransferRepairColdNative.Outcome(fixture, deniedAccept.CommandId, fixture.DestinationPartition).AsSpan().SequenceEqual(acceptBytes)).IsTrue();
        await Assert.That(RemoteTransferRepairColdNative.Outcome(fixture, deniedComplete.CommandId, fixture.SourcePartition).AsSpan().SequenceEqual(completeBytes)).IsTrue();
        await RemoteTransferCoordinationColdAssertions.EqualAsync(RemoteTransferRepairColdNative.Apply(fixture, completedCommand).Get<CommitReceipt>(), completed);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Database.InspectQueueTransfer(RemoteTransferRepairColdProtocol.Subject,
            fixture.SourceQueue, transfer.TransferId), original with { State = QueueTransferState.Delivered, ReceiptToken = proof.ReceiptToken });
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Database.InspectQueueTransferReceipt(RemoteTransferRepairColdProtocol.Subject,
            fixture.DestinationQueue, fixture.SourceQueue, transfer.TransferId), proof);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Database.InspectMessage(RemoteTransferRepairColdProtocol.Subject,
            fixture.DestinationQueue, RemoteTransferRepairColdProtocol.Message), new MessageInspection(new(RemoteTransferRepairColdProtocol.Message,
                MessageState.Acked, RemoteTransferRepairColdProtocol.ClaimedAttempts, RemoteTransferRepairColdProtocol.AckedStateVersion,
                RemoteTransferRepairColdProtocol.InitialReadySequence, null, null, LeaseVersion: RemoteTransferRepairColdProtocol.OriginalLeaseVersion), null, null));
        await Assert.That(fixture.TargetCapacity(fixture.DestinationQueue).StoredRecords).IsEqualTo(RemoteTransferRepairColdProtocol.InitialRecords);
        var expectedRecords = ceiling == RemoteTransferRepairColdProtocol.FullCeiling
            ? RemoteTransferRepairColdProtocol.FullRepairRecords : RemoteTransferRepairColdProtocol.LimitedRepairRecords;
        await RemoteTransferAttemptColdNative.AccountingAsync(fixture, transfer.TransferId, expectedRecords);
        var state = RemoteTransferRepairColdNative.State(fixture, transfer.TransferId);
        await Assert.That(state.Ceiling).IsEqualTo(ceiling);
        await Assert.That(state.AcceptPolicyGeneration).IsEqualTo(RemoteTransferRepairColdProtocol.RepairedGeneration);
        await Assert.That(state.CompleteGeneration).IsEqualTo(ceiling == RemoteTransferRepairColdProtocol.FullCeiling
            ? RemoteTransferRepairColdProtocol.RepairedGeneration : RemoteTransferRepairColdProtocol.FirstGeneration);
        await Assert.That((long)state.History.Length).IsEqualTo(expectedRecords - RemoteTransferRepairColdProtocol.InitialRecords);
        if (refused is not null)
        {
            await RemoteTransferCoordinationColdAssertions.EqualAsync(RemoteTransferRepairColdNative.Apply(fixture, refused.Command), refused.Result);
            await Assert.That(RemoteTransferRepairColdNative.Outcome(fixture, refused.Command.CommandId,
                fixture.SourcePartition).AsSpan().SequenceEqual(refused.Bytes)).IsTrue();
        }
        var stored = NativeSerialization.Deserialize<StoredOutcome>(RemoteTransferRepairColdNative.Outcome(fixture,
            acceptedCommand.CommandId, fixture.DestinationPartition));
        await RemoteTransferCoordinationColdAssertions.EqualAsync(stored.Result.Get<CommitReceipt>(), accepted);
    }
}
