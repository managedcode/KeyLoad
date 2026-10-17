using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class RemoteTransferAckedRetentionColdAssertions
{
    internal static async Task RequireAsync(RemoteTransferDatabase fixture, CreateQueueTransfer original,
        CreateQueueTransfer refusedTransfer, QueueTransferInspection source, QueueTransferInspection refusedSource,
        QueueTransferReceiptInspection proof, MessageInspection message, RemoteTransferCapacity capacity,
        CommandRequest accept, CommitReceipt accepted, CommandRequest refused, OperationResult failure,
        byte[] originalBytes, byte[] failureBytes)
    {
        await Assert.That(RemoteTransferStorage.IsCapacityFailure(failure)).IsTrue();
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Apply(OperationKind.Batch, accept).Get<CommitReceipt>(), accepted);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Apply(OperationKind.Batch, refused), failure);
        await Assert.That(RemoteTransferAttemptColdNative.Outcome(fixture, accept.CommandId,
            fixture.DestinationPartition).AsSpan().SequenceEqual(originalBytes)).IsTrue();
        await Assert.That(RemoteTransferAttemptColdNative.Outcome(fixture, refused.CommandId,
            fixture.DestinationPartition).AsSpan().SequenceEqual(failureBytes)).IsTrue();
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.TargetCapacity(fixture.DestinationQueue), capacity);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            original.SourceQueue, original.TransferId), source);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            refusedTransfer.SourceQueue, refusedTransfer.TransferId), refusedSource);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Database.InspectQueueTransferReceipt(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, original.SourceQueue, original.TransferId), proof);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Database.InspectMessage(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, RemoteTransferAckedRetentionColdTrial.Message), message);
        await Assert.That(fixture.Database.InspectQueueTransferReceipt(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, refusedTransfer.SourceQueue, refusedTransfer.TransferId)).IsNull();
        await Assert.That(fixture.Database.InspectMessage(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, RemoteTransferAckedRetentionColdTrial.RefusedMessage)).IsNull();
    }
}
