namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class QueueRetryRetainedConflictFlow
{
    private const string Queue = "retained-retry-queue";
    private const string Message = "later-ready";
    private const string Payload = "{\"body\":\"later\"}";

    internal static Task RunAsync(bool byteLimit) => ReplicaSubmissionOutcomeFlow.RunAsync(async owner =>
    {
        owner.Database.Configure(Queue, ResourceKind.WorkQueue);
        var lane = new QueueLaneRef(owner.Database.Partition, Queue);
        var request = new ReceiveRequest(Guid.NewGuid(), lane, MaxMessages: 1, MaxBytes: 256, LeaseSeconds: 30);
        var operation = Issue(owner, OperationKind.Receive, request, request.RequestId);
        var original = owner.Submit(operation);
        var empty = original.Get<ReceiveResult>();
        await Assert.That(empty.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(empty.Deliveries.IsEmpty).IsTrue();
        var retained = owner.Outcome(operation);
        var publish = new CommandRequest(Guid.NewGuid(), owner.Database.Partition,
            [new EnqueueMessage(Queue, Message, Payload)]);
        var producer = owner.Submit(Issue(owner, OperationKind.Batch, publish, publish.CommandId));
        await Assert.That(producer.Get<CommitReceipt>().CommandId).IsEqualTo(publish.CommandId);
        var changedRequest = byteLimit ? request with
        {
            MaxBytes = 512
        } : request with
        {
            MaxMessages = 2
        };
        var changed = Issue(owner, OperationKind.Receive, changedRequest, request.RequestId);
        var before = ReplicaSubmissionOutcomeState.Read(owner.Database);
        var refusal = Assert.ThrowsExactly<KeyLoadException>(() =>
            owner.Database.Database.PrepareQueueRetryOperation(changed));
        await Assert.That(refusal.Code).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(refusal.Message).IsEqualTo("The command ID was already used with different content.");
        await before.RequireAsync(ReplicaSubmissionOutcomeState.Read(owner.Database));
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(retained, owner.Outcome(operation));
        var prepared = owner.Database.Database.PrepareQueueRetryOperation(operation);
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(NativeSerialization.Serialize(operation), NativeSerialization.Serialize(prepared));
        await before.RequireAsync(ReplicaSubmissionOutcomeState.Read(owner.Database));
        await ReplicaSubmissionOutcomeFlow.RequireResultAsync(original, owner.Submit(operation));
        await HealthyAsync(owner, lane, request);
        await ReplicaSubmissionOutcomeFlow.RequireResultAsync(original,
            owner.Database.Database.ResolveAppliedSubmissionOutcome(operation));
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(retained, owner.Outcome(operation));
    });

    private static ReplicatedOperation Issue<T>(ReplicaSubmissionOutcomeFlow owner,
        OperationKind kind, T payload, Guid id)
        => owner.Database.Database.CreateNativeOperation(kind, id, "root",
            owner.Database.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(payload));

    private static async Task HealthyAsync(ReplicaSubmissionOutcomeFlow owner, QueueLaneRef lane, ReceiveRequest first)
    {
        var fresh = first with
        {
            RequestId = Guid.NewGuid()
        };
        var receive = Issue(owner, OperationKind.Receive, fresh, fresh.RequestId);
        var actual = owner.Submit(receive);
        var deliveries = actual.Get<ReceiveResult>();
        await Assert.That(deliveries.RequestId).IsEqualTo(fresh.RequestId);
        await Assert.That(deliveries.Deliveries.Length).IsEqualTo(1);
        var delivery = deliveries.Deliveries[0];
        var literal = new Delivery(Message, Payload, "{}", delivery.Token, 1L,
            receive.EvaluatedAt.AddSeconds(30), 1, 1L);
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(NativeSerialization.Serialize(literal), NativeSerialization.Serialize(delivery));
        await ReplicaSubmissionOutcomeFlow.RequireResultAsync(actual, owner.Submit(receive));
        var ack = new DeliveryCommand(Guid.NewGuid(), lane, delivery.Token, DeliveryAction.Ack);
        var acknowledge = Issue(owner, OperationKind.Delivery, ack, ack.CommandId);
        var acknowledged = owner.Submit(acknowledge);
        await Assert.That(acknowledged.Get<CommitReceipt>().CommandId).IsEqualTo(ack.CommandId);
        await ReplicaSubmissionOutcomeFlow.RequireResultAsync(acknowledged, owner.Submit(acknowledge));
        var healthy = first with
        {
            RequestId = Guid.NewGuid()
        };
        var final = owner.Submit(Issue(owner, OperationKind.Receive, healthy, healthy.RequestId)).Get<ReceiveResult>();
        await Assert.That(final.RequestId).IsEqualTo(healthy.RequestId);
        await Assert.That(final.Deliveries.IsEmpty).IsTrue();
    }
}
