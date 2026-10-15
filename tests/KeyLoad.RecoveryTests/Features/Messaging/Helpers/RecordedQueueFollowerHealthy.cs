using KeyLoad.Core;
using static KeyLoad.RecoveryTests.Features.Messaging.RecordedQueueFollowerProtocol;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class RecordedQueueFollowerHealthy
{
    internal static async Task RunAsync(PeerDiscoveryNativeCluster cluster, PeerDiscoveryNativeNode leader,
        List<RecordedQueueFollowerOutcome> retained, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var message = new EnqueueMessage(Queue, HealthyMessage, Payload, Headers);
        var enqueue = new CommandRequest(Guid.NewGuid(), Partition, [message]);
        var created = await RecordedQueueFollowerNative.SubmitAsync(cluster, leader, OperationKind.Batch, enqueue, enqueue.CommandId, token);
        retained.Add(created);
        await RecordedQueueFollowerAssertions.ReceiptAsync(created, EnqueueMutation, InitialStateVersion, HealthyMessage);
        var body = new MessageBody(HealthyMessage, Payload, Headers, message.OrderingKey, JsonData.Fingerprint(message));
        var bytes = NativeSerialization.Serialize(body).LongLength;
        await RecordedQueueFollowerAssertions.StateAsync(cluster,
            new(HealthyMessage, MessageState.Ready, NoAttempts, InitialStateVersion, HealthyReadySequence, null, null), body,
            new(SingleMessageCount, bytes, NoUsage, NoUsage, HealthyReadySequence), ReadyFamily, HealthyReadySequence);
        var request = new ReceiveRequest(Guid.NewGuid(), Lane);
        var claim = await RecordedQueueFollowerNative.SubmitAsync(cluster, leader, OperationKind.Receive, request, request.RequestId, token);
        retained.Add(claim);
        var delivery = await RecordedQueueFollowerAssertions.ClaimAsync(cluster, claim, HealthyMessage);
        var metadata = new MessageMetadata(HealthyMessage, MessageState.Leased, FirstAttempt, HealthyClaimedStateVersion,
            HealthyReadySequence, null, null, PeerDiscoveryNativeClockProfile.PrincipalId, InitialLeaseVersion,
            delivery.LeaseUntil, InitialDeliveryGeneration);
        await RecordedQueueFollowerAssertions.StateAsync(cluster, metadata, body,
            new(SingleMessageCount, bytes, SingleMessageCount, bytes, HealthyReadySequence), LeasedFamily, delivery.LeaseUntil);
        var ack = new DeliveryCommand(Guid.NewGuid(), Lane, delivery.Token, DeliveryAction.Ack);
        var completed = await RecordedQueueFollowerNative.SubmitAsync(cluster, leader, OperationKind.Delivery, ack, ack.CommandId, token);
        retained.Add(completed);
        await RecordedQueueFollowerAssertions.ReceiptAsync(completed, nameof(DeliveryAction.Ack), HealthyAckedStateVersion, HealthyMessage);
        await RecordedQueueFollowerAssertions.StateAsync(cluster,
            metadata with
            {
                State = MessageState.Acked,
                StateVersion = HealthyAckedStateVersion,
                LeaseOwner = null,
                LeaseUntil = null
            }, null,
            new(NoUsage, NoUsage, NoUsage, NoUsage, HealthyReadySequence));
    }
}
