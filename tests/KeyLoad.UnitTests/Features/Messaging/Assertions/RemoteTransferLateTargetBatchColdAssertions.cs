using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class RemoteTransferLateTargetBatchColdAssertions
{
    private const int NoAttempts = 0;
    private const long InitialVersion = 1;
    private const long FirstSequence = 1;
    private const long SecondSequence = 2;
    private const long ThirdSequence = 3;
    private const long NoUsage = 0;

    internal static async Task DeniedAsync(RemoteTransferDatabase fixture, RemoteTransferLateTargetBatchState state)
    {
        await FailureAsync(fixture, state);
        await Assert.That(RemoteTransferLateTargetBatchColdTrial.CounterBytes(fixture).AsSpan().SequenceEqual(state.OriginalCounters)).IsTrue();
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.TargetCapacity(fixture.DestinationQueue), new RemoteTransferCapacity(NoUsage, NoUsage));
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            fixture.SourceQueue, state.Transfer.TransferId), state.Pending);
        await Assert.That(fixture.Database.InspectQueueTransferReceipt(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, fixture.SourceQueue, state.Transfer.TransferId)).IsNull();
        await Assert.That(fixture.Database.InspectMessage(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, RemoteTransferLateTargetBatchColdTrial.Transferred)).IsNull();
        await MessageAsync(fixture, RemoteTransferLateTargetBatchColdTrial.Occupied, RemoteTransferLateTargetBatchColdTrial.OriginalPayload, FirstSequence);
    }

    internal static async Task HealthyAsync(RemoteTransferDatabase fixture, RemoteTransferLateTargetBatchState state,
        QueueTransferReceiptInspection receipt, byte[] counterBytes)
    {
        await FailureAsync(fixture, state);
        await Assert.That(RemoteTransferLateTargetBatchColdTrial.CounterBytes(fixture).AsSpan().SequenceEqual(counterBytes)).IsTrue();
        var counters = NativeSerialization.Deserialize<QueueCounters>(counterBytes);
        await Assert.That(counters.StoredMessages).IsEqualTo(ThirdSequence);
        await Assert.That(counters.NextReadySequence).IsEqualTo(ThirdSequence);
        await Assert.That(counters.InFlightMessages).IsEqualTo(NoUsage);
        await Assert.That(counters.InFlightBytes).IsEqualTo(NoUsage);
        var bodyBytes = fixture.Store.Read(view => new[] { RemoteTransferLateTargetBatchColdTrial.Occupied,
            RemoteTransferLateTargetBatchColdTrial.Transferred, RemoteTransferLateTargetBatchColdTrial.Derived }
            .Sum(id => (long)view.ReadOwnedValue(KeySpace.Partition(PartitionRecordFamilies.MessageBody,
                fixture.DestinationPartition, fixture.DestinationQueue.Queue, id))!.Length));
        await Assert.That(counters.StoredBytes).IsEqualTo(bodyBytes);
        await Assert.That(fixture.TargetCapacity(fixture.DestinationQueue).StoredRecords).IsEqualTo(InitialVersion);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            fixture.SourceQueue, state.Transfer.TransferId), state.Pending with { State = QueueTransferState.Delivered, ReceiptToken = receipt.ReceiptToken });
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Database.InspectQueueTransferReceipt(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, fixture.SourceQueue, state.Transfer.TransferId), receipt);
        await MessageAsync(fixture, RemoteTransferLateTargetBatchColdTrial.Occupied, RemoteTransferLateTargetBatchColdTrial.OriginalPayload, FirstSequence);
        await MessageAsync(fixture, RemoteTransferLateTargetBatchColdTrial.Transferred, RemoteTransferLateTargetBatchColdTrial.TransferPayload, SecondSequence);
        await MessageAsync(fixture, RemoteTransferLateTargetBatchColdTrial.Derived, RemoteTransferLateTargetBatchColdTrial.DerivedPayload, ThirdSequence);
    }

    private static async Task FailureAsync(RemoteTransferDatabase fixture, RemoteTransferLateTargetBatchState state)
    {
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Apply(OperationKind.Batch, state.FailedRequest), state.Failure);
        await Assert.That(RemoteTransferAttemptColdNative.Outcome(fixture, state.FailedRequest.CommandId,
            fixture.DestinationPartition).AsSpan().SequenceEqual(state.FailureBytes)).IsTrue();
    }

    private static async Task MessageAsync(RemoteTransferDatabase fixture, string id, string payload, long sequence)
        => await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Database.InspectMessage(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, id), new MessageInspection(new(id, MessageState.Ready, NoAttempts, InitialVersion, sequence, null, null),
                payload, RemoteTransferLateTargetBatchColdTrial.Headers));
}
