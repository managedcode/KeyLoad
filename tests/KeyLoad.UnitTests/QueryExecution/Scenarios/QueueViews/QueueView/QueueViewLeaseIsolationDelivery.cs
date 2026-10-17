using static KeyLoad.UnitTests.Features.QueryExecution.QueueViewLeaseIsolationProtocol;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueueViewLeaseIsolationDelivery
{
    internal static async Task RenewAsync(QueueViewLeaseIsolationState state)
    {
        var request = new DeliveryCommand(Guid.NewGuid(), state.Lane, state.Delivery.Token,
            DeliveryAction.Renew, LeaseSeconds);
        var expected = QueueViewLeaseIsolationOperations.Receipt(state, request.CommandId,
            [new(RenewKind, Queue, First, Renewed)]);
        var history = await QueueViewLeaseIsolationOperations.SendAsync(state, OperationKind.Delivery,
            request, request.CommandId, expected);
        state.CurrentDeadline = history.Operation.EvaluatedAt.AddSeconds(LeaseSeconds);
        state.Version = Renewed;
        await Assert.That(state.CurrentDeadline).IsGreaterThan(state.Delivery.LeaseUntil);
        await QueueViewLeaseIsolationMessages.RequireAsync(state);
        await QueueViewLeaseIsolationClaims.ActiveReplayAsync(state);
    }

    internal static async Task AckAsync(QueueViewLeaseIsolationState state)
    {
        var request = new DeliveryCommand(Guid.NewGuid(), state.Lane, state.Delivery.Token, DeliveryAction.Ack);
        var expected = QueueViewLeaseIsolationOperations.Receipt(state, request.CommandId,
            [new(AckKind, Queue, First, Acked)]);
        await QueueViewLeaseIsolationOperations.SendAsync(state, OperationKind.Delivery,
            request, request.CommandId, expected);
        state.Version = Acked;
        state.Acked = true;
        await QueueViewLeaseIsolationMessages.RequireAsync(state);
    }

    internal static async Task HealthyAsync(QueueViewLeaseIsolationState state)
    {
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, state.Partition,
            [new EnqueueMessage(Queue, Healthy, HealthyPayload, HealthyHeaders, NotBefore)]);
        var expected = QueueViewLeaseIsolationOperations.Receipt(state, id,
            [new(EnqueueKind, Queue, Healthy, Initial)]);
        await QueueViewLeaseIsolationOperations.SendAsync(state, OperationKind.Batch, request, id, expected);
        state.Healthy = true;
        await QueueViewLeaseIsolationMessages.RequireAsync(state);
    }
}
