using KeyLoad.Core;
using KeyLoad.CrashHost;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class QueueLeaseAuthorityProcessHealthy
{
    internal static async Task<List<(ReplicatedOperation Operation, CommitReceipt Receipt)>> RunAsync(DatabaseEngine database,
        ReplicatedOperation original, CommitReceipt receipt, DateTimeOffset retryAt, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var outcomes = new List<(ReplicatedOperation Operation, CommitReceipt Receipt)> { (original, receipt) };
        await ReplayAsync(database, outcomes);
        foreach (var id in new[] { QueueOrderedRetryCrashProtocol.First, QueueOrderedRetryCrashProtocol.Second })
        {
            await CompleteAsync(database, retryAt, id, outcomes);
        }
        await QueueOrderedRetryRecoveryTerminal.AssertAsync(database, healthy: false);
        var commandId = Guid.NewGuid();
        var producer = database.NormalizeOperation(CrashDatabase.Operation(OperationKind.Batch,
            new CommandRequest(commandId, QueueOrderedRetryCrashProtocol.Partition,
                [QueueOrderedRetryCrashSeed.Enqueue(QueueOrderedRetryCrashProtocol.Healthy)]), commandId) with
        {
            EvaluatedAt = retryAt
        });
        outcomes.Add((producer, database.Apply(producer).Get<CommitReceipt>()));
        await QueueOrderedRetryRecoveryImage.MessageAsync(database,
            new(QueueOrderedRetryCrashProtocol.Partition, QueueOrderedRetryCrashProtocol.Queue),
            new(QueueOrderedRetryCrashProtocol.Healthy, MessageState.Ready, 0, 1, 4, null, null,
                EnqueueSequence: 3, ActiveOrderSequence: 3), retained: true);
        await CompleteAsync(database, retryAt, QueueOrderedRetryCrashProtocol.Healthy, outcomes);
        await QueueOrderedRetryRecoveryTerminal.AssertAsync(database, healthy: true);
        await QueueLeaseAuthorityProcessImage.EmptyIndexesAsync(database);
        await ReplayAsync(database, outcomes);
        return outcomes;
    }

    internal static async Task ReplayAsync(DatabaseEngine database, List<(ReplicatedOperation Operation, CommitReceipt Receipt)> outcomes)
    {
        foreach (var item in outcomes)
        {
            await MessagingRecoveryReceiptAssertions.AssertSameNativeValueAsync(item.Receipt, database.Apply(item.Operation).Get<CommitReceipt>());
            await MessagingRecoveryReceiptAssertions.AssertSameNativeValueAsync(item.Receipt, database.ResolveOutcome(item.Operation).Get<CommitReceipt>());
        }
    }

    private static async Task CompleteAsync(DatabaseEngine database, DateTimeOffset at, string id,
        List<(ReplicatedOperation Operation, CommitReceipt Receipt)> outcomes)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), new(QueueOrderedRetryCrashProtocol.Partition, QueueOrderedRetryCrashProtocol.Queue), MaxMessages: 1);
        var operation = database.PrepareQueueRetryOperation(database.NormalizeOperation(
            CrashDatabase.Operation(OperationKind.Receive, request, request.RequestId) with
            {
                EvaluatedAt = at
            }));
        var delivery = await Assert.That(database.Apply(operation).Get<ReceiveResult>().Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(id);
        await Assert.That(delivery.PayloadJson).IsEqualTo(QueueOrderedRetryCrashProtocol.Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(QueueOrderedRetryCrashProtocol.Headers);
        var command = new DeliveryCommand(Guid.NewGuid(), request.Lane, delivery.Token, DeliveryAction.Ack);
        var acknowledged = database.NormalizeOperation(CrashDatabase.Operation(OperationKind.Delivery, command, command.CommandId) with
        {
            EvaluatedAt = at
        });
        outcomes.Add((acknowledged, database.Apply(acknowledged).Get<CommitReceipt>()));
    }
}
