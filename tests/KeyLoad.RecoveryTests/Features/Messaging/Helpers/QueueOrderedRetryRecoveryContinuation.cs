using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class QueueOrderedRetryRecoveryContinuation
{
    internal static async Task RunAsync(string root, DatabaseEngine database, ZoneTreeStore store, ReplicatedOperation original,
        CommitReceipt receipt, DateTimeOffset retryAt, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var outcomes = new List<(ReplicatedOperation Operation, CommitReceipt Receipt)> { (original, receipt) };
        foreach (var id in new[] { QueueOrderedRetryCrashProtocol.First, QueueOrderedRetryCrashProtocol.Second })
        { await CompleteAsync(database, retryAt, id, outcomes); }
        await QueueOrderedRetryRecoveryTerminal.AssertAsync(database, healthy: false);
        await ReplayAsync(database, outcomes);
        var image = Capture(store);
        store.Dispose();
        using var reopened = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        var cold = QueueLifecycleRecoveryAssertions.Open(reopened);
        await Assert.That(Capture(reopened)).IsEquivalentTo(image, CollectionOrdering.Matching);
        await ReplayAsync(cold, outcomes);
        var commandId = Guid.NewGuid();
        var operation = cold.NormalizeOperation(CrashDatabase.Operation(OperationKind.Batch,
            new CommandRequest(commandId, QueueOrderedRetryCrashProtocol.Partition,
                [QueueOrderedRetryCrashSeed.Enqueue(QueueOrderedRetryCrashProtocol.Healthy)]), commandId) with
        { EvaluatedAt = retryAt });
        outcomes.Add((operation, cold.Apply(operation).Get<CommitReceipt>()));
        await CompleteAsync(cold, retryAt, QueueOrderedRetryCrashProtocol.Healthy, outcomes);
        await QueueOrderedRetryRecoveryTerminal.AssertAsync(cold, healthy: true);
        await ReplayAsync(cold, outcomes);
    }

    private static async Task CompleteAsync(DatabaseEngine database, DateTimeOffset at, string id,
        List<(ReplicatedOperation Operation, CommitReceipt Receipt)> outcomes)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), new(QueueOrderedRetryCrashProtocol.Partition, QueueOrderedRetryCrashProtocol.Queue),
            MaxMessages: QueueOrderedRetryCrashProtocol.One);
        var operation = database.PrepareQueueRetryOperation(database.NormalizeOperation(
            CrashDatabase.Operation(OperationKind.Receive, request, request.RequestId) with { EvaluatedAt = at }));
        var delivery = await Assert.That(database.Apply(operation).Get<ReceiveResult>().Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(id);
        await Assert.That(delivery.PayloadJson).IsEqualTo(QueueOrderedRetryCrashProtocol.Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(QueueOrderedRetryCrashProtocol.Headers);
        var command = new DeliveryCommand(Guid.NewGuid(), request.Lane, delivery.Token, DeliveryAction.Ack);
        var acknowledged = database.NormalizeOperation(CrashDatabase.Operation(OperationKind.Delivery, command, command.CommandId) with { EvaluatedAt = at });
        outcomes.Add((acknowledged, database.Apply(acknowledged).Get<CommitReceipt>()));
    }
    private static async Task ReplayAsync(DatabaseEngine database, List<(ReplicatedOperation Operation, CommitReceipt Receipt)> outcomes)
    {
        foreach (var item in outcomes)
        { await MessagingRecoveryReceiptAssertions.AssertSameNativeValueAsync(item.Receipt, database.Apply(item.Operation).Get<CommitReceipt>()); }
    }
    private static string[] Capture(ZoneTreeStore store) => store.Read(view => QueueOrderedRetryCrashProtocol.Families.SelectMany(family =>
    {
        var page = view.Scan(KeySpace.Partition(family, QueueOrderedRetryCrashProtocol.Partition, QueueOrderedRetryCrashProtocol.Queue),
            QueueOrderedRetryCrashProtocol.ImageRecords);
        if (page.HasMore)
        { throw new InvalidOperationException("The ordered retry image exceeded its original bound."); }
        return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span));
    }).ToArray());
}
