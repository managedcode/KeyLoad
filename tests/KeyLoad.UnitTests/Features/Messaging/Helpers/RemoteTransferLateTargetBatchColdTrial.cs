using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class RemoteTransferLateTargetBatchColdTrial
{
    internal const string Occupied = "late-target-occupied";
    internal const string Transferred = "late-target-transfer";
    internal const string Derived = "late-target-derived";
    internal const string OriginalPayload = "{\"occupied\":true}";
    internal const string TransferPayload = "{\"atomic\":true}";
    internal const string DerivedPayload = "{\"derived\":true}";
    internal const string Headers = "{}";
    private const string Missing = "The original native queue counter is missing.";

    internal static async Task RunAsync()
    {
        using var fixture = new RemoteTransferDatabase();
        fixture.Commit(fixture.DestinationPartition, new EnqueueMessage(fixture.DestinationQueue.Queue, Occupied, OriginalPayload));
        var transfer = new CreateQueueTransfer(fixture.SourceQueue, Guid.NewGuid(), fixture.DestinationQueue,
            new(fixture.DestinationQueue.Queue, Transferred, TransferPayload));
        fixture.Commit(fixture.SourcePartition, transfer);
        var pending = fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal, fixture.SourceQueue, transfer.TransferId)!;
        var counters = CounterBytes(fixture);
        var failedRequest = new CommandRequest(Guid.NewGuid(), fixture.DestinationPartition,
            [new AcceptQueueTransfer(fixture.DestinationQueue, pending.IntentToken),
             new EnqueueMessage(fixture.DestinationQueue.Queue, Occupied, DerivedPayload)]);
        var failure = fixture.Apply(OperationKind.Batch, failedRequest);
        await Assert.That(failure.Error).IsEqualTo(ErrorCode.Conflict);
        var state = new RemoteTransferLateTargetBatchState(transfer, pending, failedRequest, failure,
            RemoteTransferAttemptColdNative.Outcome(fixture, failedRequest.CommandId, fixture.DestinationPartition), counters);
        await RemoteTransferLateTargetBatchColdAssertions.DeniedAsync(fixture, state);
        fixture.Reopen();
        await RemoteTransferLateTargetBatchColdAssertions.DeniedAsync(fixture, state);
        var repaired = new CommandRequest(Guid.NewGuid(), fixture.DestinationPartition,
            [new AcceptQueueTransfer(fixture.DestinationQueue, pending.IntentToken),
             new EnqueueMessage(fixture.DestinationQueue.Queue, Derived, DerivedPayload)]);
        var accepted = fixture.Apply(OperationKind.Batch, repaired).Get<CommitReceipt>();
        var receipt = fixture.Database.InspectQueueTransferReceipt(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, fixture.SourceQueue, transfer.TransferId)!;
        await Assert.That(receipt.TargetCommit).IsEqualTo(accepted.Token);
        var complete = new CommandRequest(Guid.NewGuid(), fixture.SourcePartition,
            [new CompleteQueueTransfer(fixture.SourceQueue, transfer.TransferId, receipt.ReceiptToken)]);
        var completed = fixture.Apply(OperationKind.Batch, complete).Get<CommitReceipt>();
        var committedCounters = CounterBytes(fixture);
        await RemoteTransferLateTargetBatchColdAssertions.HealthyAsync(fixture, state, receipt, committedCounters);
        fixture.Reopen();
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Apply(OperationKind.Batch, repaired).Get<CommitReceipt>(), accepted);
        await RemoteTransferCoordinationColdAssertions.EqualAsync(fixture.Apply(OperationKind.Batch, complete).Get<CommitReceipt>(), completed);
        await RemoteTransferLateTargetBatchColdAssertions.HealthyAsync(fixture, state, receipt, committedCounters);
    }

    internal static byte[] CounterBytes(RemoteTransferDatabase fixture)
        => fixture.Store.Read(view => view.ReadOwnedValue(KeySpace.Partition(PartitionRecordFamilies.QueueCounters,
            fixture.DestinationPartition, fixture.DestinationQueue.Queue))) ?? throw new InvalidOperationException(Missing);
}
