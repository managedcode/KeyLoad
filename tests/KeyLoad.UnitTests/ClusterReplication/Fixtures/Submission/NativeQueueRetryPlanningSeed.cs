using A = KeyLoad.UnitTests.Features.ClusterReplication.NativeSubmissionPlanningBudgetAssertions;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class NativeQueueRetryPlanningSeed
{
    internal const string Queue = "planning-budget-queue";
    internal const string Message = "planning-budget-message";
    internal const string Payload = """{"value":7}""";

    internal static async Task<(ReplicatedOperation Operation, QueueLaneRef Lane, QueuePolicy Policy)> CreateAsync(
        ReplicaSubmissionOutcomeFlow owner, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var fixture = owner.Database;
        var policy = new QueuePolicy { RetryJitter = QueueRetryJitter.Full };
        var definition = new ResourceDefinition(Queue, ResourceKind.WorkQueue, fixture.Partition.TransactionDomainId)
        {
            QueuePolicy = policy
        };
        await A.EqualAsync(definition, fixture.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(fixture.Partition.TenantId, fixture.Partition.DatabaseId, definition)).Get<ResourceDefinition>());
        var publish = new CommandRequest(Guid.NewGuid(), fixture.Partition, [new EnqueueMessage(Queue, Message, Payload)]);
        var expected = A.Receipt(owner, publish.CommandId, new("enqueue", Queue, Message, 1));
        await A.ReceiptAsync(expected, owner.Submit(A.Issue(owner, OperationKind.Batch, publish, publish.CommandId)).Get<CommitReceipt>());
        var lane = new QueueLaneRef(fixture.Partition, Queue);
        var receive = new ReceiveRequest(Guid.NewGuid(), lane, MaxMessages: 1, MaxBytes: 256, LeaseSeconds: policy.MaxLeaseSeconds);
        var receiveOperation = A.Issue(owner, OperationKind.Receive, receive, receive.RequestId);
        var next = A.NextToken(owner);
        var received = owner.Submit(receiveOperation).Get<ReceiveResult>();
        var delivery = await Assert.That(received.Deliveries).HasSingleItem();
        var expectedReceive = new ReceiveResult(receive.RequestId,
            [new(Message, Payload, "{}", delivery.Token, 1,
                receiveOperation.EvaluatedAt.AddSeconds(policy.MaxLeaseSeconds), 1, 1)], next);
        await A.EqualAsync(expectedReceive, received);
        var nack = new DeliveryCommand(Guid.NewGuid(), lane, delivery.Token, DeliveryAction.Nack);
        return (A.Issue(owner, OperationKind.Delivery, nack, nack.CommandId), lane, policy);
    }
}
