using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using TUnit.Assertions.Enums;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class QueueClaimAckRecoveryContinuation
{
    internal static async Task ColdAsync(string root, DatabaseEngine restored, ZoneTreeStore cold,
        Command<DatabaseCommand> operation, bool ack, Result<object> result, Delivery? delivery, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!ack)
        {
            await Assert.That(TimeProvider.System.GetUtcNow()).IsLessThan(delivery!.LeaseUntil);
        }
        await QueueClaimAckRecovery.ReplayAsync(restored, cold, operation, result, token);
        await QueueClaimAckRecoveryAssertions.ImageAsync(restored, QueueClaimAckCrashProtocol.Message,
            QueueClaimAckRecovery.RecoveredState(ack, true), delivery, 1);
        if (!ack)
        {
            var command = new DeliveryCommand(Guid.NewGuid(), QueueClaimAckCrashProtocol.Lane, delivery!.Token, DeliveryAction.Ack);
            var completion = restored.OwnCommand(CrashDatabase.Operation(restored, OperationKind.Delivery, command, command.CommandId));
            token.ThrowIfCancellationRequested();
            var target = QueueClaimAckRecovery.NextToken(restored, cold);
            var receipt = restored.Apply(completion);
            await QueueClaimAckRecovery.ReceiptAsync(receipt.Get<CommitReceipt>(), command.CommandId,
                QueueClaimAckCrashProtocol.Message, target);
            await QueueClaimAckRecovery.ReplayAsync(restored, cold, completion, receipt, token);
        }
        await QueueClaimAckRecoveryAssertions.ImageAsync(restored, QueueClaimAckCrashProtocol.Message, MessageState.Acked, delivery, 1);
        await HealthyAsync(root, restored, cold, token);
    }

    private static async Task HealthyAsync(string root, DatabaseEngine database, ZoneTreeStore store, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var id = Guid.NewGuid();
        var enqueue = database.OwnCommand(CrashDatabase.Operation(database, OperationKind.Batch,
            new CommandRequest(id, QueueClaimAckCrashProtocol.Partition, [new EnqueueMessage(QueueClaimAckCrashProtocol.Queue,
                QueueClaimAckCrashProtocol.Healthy, QueueClaimAckCrashProtocol.Payload, QueueClaimAckCrashProtocol.Headers)]), id));
        var enqueueTarget = QueueClaimAckRecovery.NextToken(database, store);
        var created = database.Apply(enqueue);
        await EnqueueReceiptAsync(created.Get<CommitReceipt>(), enqueue.CommandId, enqueueTarget);
        await QueueClaimAckRecoveryAssertions.ImageAsync(database, QueueClaimAckCrashProtocol.Healthy, MessageState.Ready, null, 2);
        await QueueClaimAckRecovery.ReplayAsync(database, store, enqueue, created, token);
        var request = new ReceiveRequest(Guid.NewGuid(), QueueClaimAckCrashProtocol.Lane, MaxMessages: 1,
            LeaseSeconds: QueueClaimAckCrashProtocol.LeaseSeconds);
        var claim = database.OwnCommand(CrashDatabase.Operation(database, OperationKind.Receive, request, request.RequestId));
        var claimTarget = QueueClaimAckRecovery.NextToken(database, store);
        var received = database.Apply(claim);
        await QueueClaimAckRecoveryAssertions.DeliveryAsync(database, received.Get<ReceiveResult>(), request.RequestId,
            new DateTimeOffset(claim.Timestamp), QueueClaimAckCrashProtocol.Healthy, claimTarget);
        var delivery = received.Get<ReceiveResult>().Deliveries.Single();
        await QueueClaimAckRecoveryAssertions.ImageAsync(database, QueueClaimAckCrashProtocol.Healthy, MessageState.Leased, delivery, 2);
        await QueueClaimAckRecovery.ReplayAsync(database, store, claim, received, token);
        await HealthyColdAsync(root, store, enqueue, created, enqueueTarget, claim, received, claimTarget, delivery, token);
    }

    private static async Task HealthyColdAsync(string root, ZoneTreeStore store, Command<DatabaseCommand> enqueue,
        Result<object> created, CommitToken enqueueTarget, Command<DatabaseCommand> claim, Result<object> received,
        CommitToken claimTarget, Delivery delivery, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var image = QueueClaimAckRecoveryAssertions.Capture(store);
        var position = store.Position;
        var identity = store.Identity;
        store.Dispose();
        using var cold = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        await QueueClaimAckRecoveryAssertions.EqualAsync(cold.Identity, identity);
        await Assert.That(cold.Position).IsEqualTo(position);
        await Assert.That(QueueClaimAckRecoveryAssertions.Capture(cold)).IsEquivalentTo(image, CollectionOrdering.Matching);
        var restored = QueueLifecycleRecoveryAssertions.Open(cold);
        await Assert.That(TimeProvider.System.GetUtcNow()).IsLessThan(delivery.LeaseUntil);
        await QueueClaimAckRecovery.ReplayAsync(restored, cold, enqueue, created, token);
        await EnqueueReceiptAsync(restored.ResolveOutcome(enqueue).Get<CommitReceipt>(), enqueue.CommandId, enqueueTarget);
        await QueueClaimAckRecovery.ReplayAsync(restored, cold, claim, received, token);
        await QueueClaimAckRecoveryAssertions.DeliveryAsync(restored, restored.ResolveOutcome(claim).Get<ReceiveResult>(),
            claim.CommandId, new DateTimeOffset(claim.Timestamp), QueueClaimAckCrashProtocol.Healthy, claimTarget);
        await QueueClaimAckRecoveryAssertions.ImageAsync(restored, QueueClaimAckCrashProtocol.Healthy, MessageState.Leased, delivery, 2);
        var ack = new DeliveryCommand(Guid.NewGuid(), QueueClaimAckCrashProtocol.Lane, delivery.Token, DeliveryAction.Ack);
        var completion = restored.OwnCommand(CrashDatabase.Operation(restored, OperationKind.Delivery, ack, ack.CommandId));
        token.ThrowIfCancellationRequested();
        var target = QueueClaimAckRecovery.NextToken(restored, cold);
        var completed = restored.Apply(completion);
        await QueueClaimAckRecovery.ReceiptAsync(completed.Get<CommitReceipt>(), ack.CommandId,
            QueueClaimAckCrashProtocol.Healthy, target);
        await QueueClaimAckRecovery.ReplayAsync(restored, cold, completion, completed, token);
        await QueueClaimAckRecoveryAssertions.ImageAsync(restored, QueueClaimAckCrashProtocol.Healthy, MessageState.Acked, delivery, 2);
        await QueueClaimAckRecoveryAssertions.ImageAsync(restored, QueueClaimAckCrashProtocol.Message, MessageState.Acked, null, 1, counterSequence: 2);
        await FinalColdAsync(root, cold, enqueue, created, enqueueTarget, completion, completed, target, token);
    }

    private static async Task FinalColdAsync(string root, ZoneTreeStore store, Command<DatabaseCommand> enqueue,
        Result<object> created, CommitToken enqueueTarget, Command<DatabaseCommand> completion,
        Result<object> completed, CommitToken ackTarget, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var image = QueueClaimAckRecoveryAssertions.Capture(store);
        var position = store.Position;
        var identity = store.Identity;
        store.Dispose();
        using var cold = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        await QueueClaimAckRecoveryAssertions.EqualAsync(cold.Identity, identity);
        await Assert.That(cold.Position).IsEqualTo(position);
        await Assert.That(QueueClaimAckRecoveryAssertions.Capture(cold)).IsEquivalentTo(image, CollectionOrdering.Matching);
        var restored = QueueLifecycleRecoveryAssertions.Open(cold);
        await QueueClaimAckRecovery.ReplayAsync(restored, cold, enqueue, created, token);
        await EnqueueReceiptAsync(restored.ResolveOutcome(enqueue).Get<CommitReceipt>(), enqueue.CommandId, enqueueTarget);
        await QueueClaimAckRecovery.ReplayAsync(restored, cold, completion, completed, token);
        await QueueClaimAckRecovery.ReceiptAsync(restored.ResolveOutcome(completion).Get<CommitReceipt>(), completion.CommandId,
            QueueClaimAckCrashProtocol.Healthy, ackTarget);
        await QueueClaimAckRecoveryAssertions.ImageAsync(restored, QueueClaimAckCrashProtocol.Healthy, MessageState.Acked, null, 2);
        await QueueClaimAckRecoveryAssertions.ImageAsync(restored, QueueClaimAckCrashProtocol.Message, MessageState.Acked, null, 1, counterSequence: 2);
    }

    private static Task EnqueueReceiptAsync(CommitReceipt receipt, Guid commandId, CommitToken target)
        => QueueClaimAckRecoveryAssertions.EqualAsync(receipt, new CommitReceipt(commandId, target, [new("enqueue", QueueClaimAckCrashProtocol.Queue, QueueClaimAckCrashProtocol.Healthy, 1)], DurabilityProfile.ProcessDurable, 1));

}
