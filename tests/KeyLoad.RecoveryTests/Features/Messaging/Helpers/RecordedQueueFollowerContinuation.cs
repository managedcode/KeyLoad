using static KeyLoad.RecoveryTests.Features.Messaging.RecordedQueueFollowerProtocol;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class RecordedQueueFollowerContinuation
{
    internal static async Task RunAsync(PeerDiscoveryNativeCluster cluster, PeerDiscoveryNativeNode leader,
        MessageBody body, long bytes, List<RecordedQueueFollowerOutcome> retained, CancellationToken token)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), Lane);
        var claim = await RecordedQueueFollowerNative.SubmitAsync(cluster, leader, OperationKind.Receive, request, request.RequestId, token);
        retained.Add(claim);
        var delivery = await RecordedQueueFollowerAssertions.ClaimAsync(cluster, claim);
        var metadata = new MessageMetadata(Message, MessageState.Leased, FirstAttempt, ClaimedStateVersion,
            FirstReadySequence, null, null, PeerDiscoveryNativeClockProfile.PrincipalId, InitialLeaseVersion,
            delivery.LeaseUntil, InitialDeliveryGeneration);
        var counters = new QueueCounters(SingleMessageCount, bytes, SingleMessageCount, bytes, FirstReadySequence);
        await RecordedQueueFollowerAssertions.StateAsync(cluster, metadata, body, counters, LeasedFamily, delivery.LeaseUntil);
        var stale = new CommandRequest(Guid.NewGuid(), Partition,
            [new AdvanceQueueDeadline(Queue, Message, MessageState.Leased, ClaimedStateVersion, InitialLeaseVersion,
                delivery.LeaseUntil, QueueDeadlineKind.ExpireLease)]);
        var renew = new DeliveryCommand(Guid.NewGuid(), Lane, delivery.Token, DeliveryAction.Renew);
        var renewed = await RecordedQueueFollowerNative.SubmitAsync(cluster, leader, OperationKind.Delivery, renew, renew.CommandId, token);
        retained.Add(renewed);
        await RecordedQueueFollowerAssertions.ReceiptAsync(renewed, nameof(DeliveryAction.Renew), RenewedStateVersion);
        var deadline = renewed.Entry.Operation!.EvaluatedAt.AddSeconds(LeaseSeconds);
        metadata = metadata with
        {
            StateVersion = RenewedStateVersion,
            LeaseUntil = deadline
        };
        await RecordedQueueFollowerAssertions.StateAsync(cluster, metadata, body, counters, LeasedFamily, deadline);
        var refused = await RecordedQueueFollowerNative.SubmitAsync(cluster, leader, OperationKind.Batch, stale, stale.CommandId, token);
        retained.Add(refused);
        await Assert.That(refused.Result.Error).IsEqualTo(ErrorCode.RevisionConflict);
        await Assert.That(refused.Result.SafeDetail).IsEqualTo(StaleDeadlineDetail);
        await Assert.That(refused.Result.Json).IsNull();
        await Assert.That(refused.Result.NativeValue).IsNull();
        await RecordedQueueFollowerAssertions.StateAsync(cluster, metadata, body, counters, LeasedFamily, deadline);
        var ack = new DeliveryCommand(Guid.NewGuid(), Lane, delivery.Token, DeliveryAction.Ack);
        var completed = await RecordedQueueFollowerNative.SubmitAsync(cluster, leader, OperationKind.Delivery, ack, ack.CommandId, token);
        retained.Add(completed);
        await RecordedQueueFollowerAssertions.ReceiptAsync(completed, nameof(DeliveryAction.Ack), AckedStateVersion);
        metadata = metadata with
        {
            State = MessageState.Acked,
            StateVersion = AckedStateVersion,
            LeaseOwner = null,
            LeaseUntil = null
        };
        await RecordedQueueFollowerAssertions.StateAsync(cluster, metadata, null,
            new(NoUsage, NoUsage, NoUsage, NoUsage, FirstReadySequence));
        await RecordedQueueFollowerHealthy.RunAsync(cluster, leader, retained, token);
        await RecordedQueueFollowerCold.RunAsync(cluster, retained, token);
    }
}
