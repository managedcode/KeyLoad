using KeyLoad.Core;
using static KeyLoad.RecoveryTests.Features.Messaging.RecordedQueueFollowerProtocol;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class RecordedQueueFollowerFlow
{
    internal static async Task RunAsync(CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(OperationBound, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var token = linked.Token;
        var clocks = PeerDiscoveryNativeCluster.Voters.ToDictionary(voter => voter, _ => new RecordedQueueFollowerClockFault());
        using var profile = new PeerDiscoveryNativeClockProfile(clocks.ToDictionary(pair => pair.Key, pair => (TimeProvider)pair.Value));
        await using var cluster = new PeerDiscoveryNativeCluster(profile);
        await cluster.InitializeAsync();
        var leader = await RecordedQueueFollowerNative.LeaderAsync(cluster, token);
        var direction = ClockFault;
        foreach (var pair in clocks.Where(pair => pair.Key != leader.Configuration.LocalId))
        {
            pair.Value.Offset = direction;
            direction = -direction;
        }
        foreach (var node in cluster.Nodes.Where(node => node.Configuration.LocalId != leader.Configuration.LocalId))
        {
            var difference = (node.Database.EvaluationClock.GetUtcNow() - leader.Database.EvaluationClock.GetUtcNow()).Duration();
            await Assert.That(difference >= ClockFault - ScheduleLead).IsTrue();
        }
        var configured = await RecordedQueueFollowerNative.SubmitAsync(cluster, leader, OperationKind.ConfigureResource,
            new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId, new(Queue, ResourceKind.WorkQueue, Partition.TransactionDomainId)),
            Guid.NewGuid(), token);
        await RecordedQueueFollowerAssertions.EqualAsync(configured.Result.Get<ResourceDefinition>(),
            new ResourceDefinition(Queue, ResourceKind.WorkQueue, Partition.TransactionDomainId));
        var notBefore = TimeProvider.System.GetUtcNow() + ScheduleLead;
        var message = new EnqueueMessage(Queue, Message, Payload, Headers, NotBefore: notBefore);
        var enqueue = new CommandRequest(Guid.NewGuid(), Partition, [message]);
        var created = await RecordedQueueFollowerNative.SubmitAsync(cluster, leader, OperationKind.Batch, enqueue, enqueue.CommandId, token);
        await RecordedQueueFollowerAssertions.ReceiptAsync(created, EnqueueMutation, InitialStateVersion);
        await Assert.That(created.Entry.Operation!.EvaluatedAt < notBefore).IsTrue();
        var body = new MessageBody(Message, Payload, Headers, message.OrderingKey, JsonData.Fingerprint(message));
        var bytes = NativeSerialization.Serialize(body).LongLength;
        await RecordedQueueFollowerAssertions.StateAsync(cluster,
            new(Message, MessageState.Scheduled, NoAttempts, InitialStateVersion, NoReadySequence, notBefore, null), body,
            new(SingleMessageCount, bytes, NoUsage, NoUsage, NoReadySequence), ScheduledFamily, notBefore);
        await RecordedQueueFollowerNative.UntilAsync(notBefore, token);
        var request = new CommandRequest(Guid.NewGuid(), Partition,
            [new AdvanceQueueDeadline(Queue, Message, MessageState.Scheduled, InitialStateVersion, NoLeaseVersion, notBefore, QueueDeadlineKind.PromoteScheduled)]);
        var promoted = await RecordedQueueFollowerNative.SubmitAsync(cluster, leader, OperationKind.Batch, request, request.CommandId, token);
        await Assert.That(promoted.Entry.Operation!.EvaluatedAt >= notBefore).IsTrue();
        await RecordedQueueFollowerAssertions.ReceiptAsync(promoted, DeadlineMutation, PromotedStateVersion);
        await RecordedQueueFollowerAssertions.StateAsync(cluster,
            new(Message, MessageState.Ready, NoAttempts, PromotedStateVersion, FirstReadySequence, null, null), body,
            new(SingleMessageCount, bytes, NoUsage, NoUsage, FirstReadySequence), ReadyFamily, FirstReadySequence);
        await RecordedQueueFollowerContinuation.RunAsync(cluster, leader, body, bytes, [created, promoted], token);
    }
}
