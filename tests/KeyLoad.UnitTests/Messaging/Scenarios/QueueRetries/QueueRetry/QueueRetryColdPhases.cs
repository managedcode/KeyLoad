using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueRetryColdPhases
{
    internal static async Task NackAsync(DatabaseEngine database, QueueRetryColdState state, Delivery delivery,
        DateTimeOffset time, DateTimeOffset? next, long version, long sequence)
    {
        var command = new DeliveryCommand(Guid.NewGuid(), state.Lane, delivery.Token, DeliveryAction.Nack);
        var result = QueueWholeFlowStorage.Apply(database, OperationKind.Delivery, command, command.CommandId, time);
        var receipt = result.Get<CommitReceipt>();
        await QueueRetryColdAssertions.DeliveryReceiptAsync(receipt, command, version);
        state.Completed.Add((command, result));
        await QueueRetryColdAssertions.PendingAsync(database, state, new(QueueRetryColdProtocol.Retry,
            next is null ? MessageState.DeadLettered : MessageState.Scheduled, delivery.Attempt, version,
            sequence, next, null, LeaseVersion: delivery.LeaseVersion,
            SafeFailureCode: next is null ? QueueRetryColdProtocol.ExhaustedCode : QueueRetryColdProtocol.RetryCode,
            ParkedSequence: next is null ? QueueRetryColdProtocol.FirstSequence : QueueRetryColdProtocol.Zero));
        await QueueRetryColdIndices.ScheduledAsync(database, state, next);
    }

    internal static async Task EmptyAsync(DatabaseEngine database, QueueRetryColdState state, DateTimeOffset time)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), state.Lane, LeaseSeconds: QueueRetryColdProtocol.LeaseSeconds);
        var actual = QueueWholeFlowStorage.Apply(database, OperationKind.Receive, request, request.RequestId, time).Get<ReceiveResult>();
        await Assert.That(actual.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(actual.Deliveries).IsEmpty();
    }

    internal static async Task ExhaustAsync(DatabaseEngine database, QueueRetryColdState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var secondAt = state.Start.AddMilliseconds(QueueRetryColdProtocol.BaseDelayMilliseconds);
        var second = await ClaimAsync(database, state, secondAt, QueueRetryColdProtocol.Two, QueueRetryColdProtocol.SecondSequence, QueueRetryColdProtocol.SecondClaimVersion);
        var lastAt = state.Start.AddMilliseconds(QueueRetryColdProtocol.SecondDueMilliseconds);
        await NackAsync(database, state, second, secondAt, lastAt, QueueRetryColdProtocol.SecondRetryVersion, QueueRetryColdProtocol.SecondSequence);
        var last = await ClaimAsync(database, state, lastAt, QueueRetryColdProtocol.MaximumAttempts, QueueRetryColdProtocol.LastSequence, QueueRetryColdProtocol.LastClaimVersion);
        await NackAsync(database, state, last, lastAt, null, QueueRetryColdProtocol.TerminalVersion, QueueRetryColdProtocol.LastSequence);
        await QueueRetryColdAssertions.TerminalAsync(database, state, false);
    }

    private static async Task<Delivery> ClaimAsync(DatabaseEngine database, QueueRetryColdState state,
        DateTimeOffset time, int attempt, long sequence, long version)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), state.Lane, LeaseSeconds: QueueRetryColdProtocol.LeaseSeconds);
        var actual = QueueWholeFlowStorage.Apply(database, OperationKind.Receive, request, request.RequestId, time).Get<ReceiveResult>();
        await Assert.That(actual.RequestId).IsEqualTo(request.RequestId);
        var delivery = await Assert.That(actual.Deliveries).HasSingleItem();
        await QueueRetryColdAssertions.DeliveryAsync(database, delivery, state, attempt, sequence, version, time);
        return delivery;
    }

    internal static async Task ExpireAndContinueAsync(DatabaseEngine database, QueueRetryColdState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await EmptyAsync(database, state, state.FinalTime);
        await QueueRetryColdAssertions.ExpiredAsync(database, state);
        var command = new CommandRequest(Guid.NewGuid(), state.Lane.Partition,
            [new EnqueueMessage(state.Lane.Queue, QueueRetryColdProtocol.Healthy, QueueRetryColdProtocol.HealthyPayload, QueueRetryColdProtocol.Headers)]);
        var original = QueueWholeFlowStorage.Apply(database, OperationKind.Batch, command, command.CommandId, state.FinalTime);
        original.Get<CommitReceipt>();
        state.Healthy = (command, original);
        var receive = new ReceiveRequest(Guid.NewGuid(), state.Lane, LeaseSeconds: QueueRetryColdProtocol.LeaseSeconds);
        var received = QueueWholeFlowStorage.Apply(database, OperationKind.Receive, receive,
            receive.RequestId, state.FinalTime).Get<ReceiveResult>();
        await Assert.That(received.RequestId).IsEqualTo(receive.RequestId);
        var delivery = await Assert.That(received.Deliveries).HasSingleItem();
        await QueueRetryColdIndices.HealthyLeasedAsync(database, state, delivery);
        await Assert.That(delivery.Id).IsEqualTo(QueueRetryColdProtocol.Healthy);
        await Assert.That(delivery.PayloadJson).IsEqualTo(QueueRetryColdProtocol.HealthyPayload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(QueueRetryColdProtocol.Headers);
        var ack = new DeliveryCommand(Guid.NewGuid(), state.Lane, delivery.Token, DeliveryAction.Ack);
        var acked = QueueWholeFlowStorage.Apply(database, OperationKind.Delivery, ack, ack.CommandId, state.FinalTime);
        await QueueRetryColdAssertions.DeliveryReceiptAsync(acked.Get<CommitReceipt>(), ack, QueueRetryColdProtocol.FirstRetryVersion);
        state.Completed.Add((ack, acked));
        await QueueRetryColdAssertions.TerminalAsync(database, state, true);
    }
}
