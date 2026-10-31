using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;
using TUnit.Assertions.Enums;

namespace KeyLoad.ProcessRecoveryTests.Messaging;

internal static class QueueOrderedRetryRecoveryContinuation
{
    internal static async Task RunAsync(string root, DatabaseEngine database, ZoneTreeStore store, Command<DatabaseCommand> original,
        CommitReceipt receipt, DateTimeOffset retryAt, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var outcomes = new List<(Command<DatabaseCommand> Operation, CommitReceipt Receipt)> { (original, receipt) };
        foreach (var id in new[] { QueueOrderedRetryCrashProtocol.First, QueueOrderedRetryCrashProtocol.Second })
        {
            await CompleteAsync(database, retryAt, id, outcomes);
        }
        await QueueOrderedRetryRecoveryTerminal.AssertAsync(database, healthy: false);
        await ReplayAsync(database, outcomes);
        var image = Capture(store);
        store.Dispose();
        using var reopened = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        var cold = QueueLifecycleRecoveryAssertions.Open(reopened);
        await Assert.That(Capture(reopened)).IsEquivalentTo(image, CollectionOrdering.Matching);
        await ReplayAsync(cold, outcomes);
        var commandId = Guid.NewGuid();
        var operation = cold.OwnCommand(cold.CreateCommand(OperationKind.Batch, commandId, CrashFixtureValues.Principal, retryAt, new CommandRequest(commandId, QueueOrderedRetryCrashProtocol.Partition,
                [QueueOrderedRetryCrashSeed.Enqueue(QueueOrderedRetryCrashProtocol.Healthy)])));
        outcomes.Add((operation, cold.Apply(operation).Get<CommitReceipt>()));
        await CompleteAsync(cold, retryAt, QueueOrderedRetryCrashProtocol.Healthy, outcomes);
        await QueueOrderedRetryRecoveryTerminal.AssertAsync(cold, healthy: true);
        await ReplayAsync(cold, outcomes);
    }

    private static async Task CompleteAsync(DatabaseEngine database, DateTimeOffset at, string id,
        List<(Command<DatabaseCommand> Operation, CommitReceipt Receipt)> outcomes)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), new(QueueOrderedRetryCrashProtocol.Partition, QueueOrderedRetryCrashProtocol.Queue),
            MaxMessages: QueueOrderedRetryCrashProtocol.One);
        var operation = database.PrepareQueueRetryOperation(database.OwnCommand(
            database.CreateCommand(OperationKind.Receive, request.RequestId, CrashFixtureValues.Principal, at, request)));
        var delivery = await Assert.That(database.Apply(operation).Get<ReceiveResult>().Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(id);
        await Assert.That(delivery.PayloadJson).IsEqualTo(QueueOrderedRetryCrashProtocol.Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(QueueOrderedRetryCrashProtocol.Headers);
        var command = new DeliveryCommand(Guid.NewGuid(), request.Lane, delivery.Token, DeliveryAction.Ack);
        var acknowledged = database.OwnCommand(database.CreateCommand(OperationKind.Delivery, command.CommandId, CrashFixtureValues.Principal, at, command));
        outcomes.Add((acknowledged, database.Apply(acknowledged).Get<CommitReceipt>()));
    }
    private static async Task ReplayAsync(DatabaseEngine database, List<(Command<DatabaseCommand> Operation, CommitReceipt Receipt)> outcomes)
    {
        foreach (var item in outcomes)
        {
            await MessagingRecoveryReceiptAssertions.AssertSameValueAsync(item.Receipt, database.Apply(item.Operation).Get<CommitReceipt>());
        }
    }
    private static string[] Capture(ZoneTreeStore store) => store.Read(view => QueueOrderedRetryCrashProtocol.Families.SelectMany(family =>
    {
        var page = view.Scan(KeySpace.Partition(family, QueueOrderedRetryCrashProtocol.Partition, QueueOrderedRetryCrashProtocol.Queue),
            QueueOrderedRetryCrashProtocol.ImageRecords);
        if (page.HasMore)
        {
            throw new InvalidOperationException("The ordered retry image exceeded its original bound.");
        }
        return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span));
    }).ToArray());
}
