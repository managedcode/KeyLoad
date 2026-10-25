using KeyLoad.Core;
using KeyLoad.Core.Messaging;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.ProcessRecoveryTests.Messaging;

internal static class QueueOrderedRetryRecovery
{
    internal static async Task VerifyAsync(string root, CommitStage stage, CancellationToken token)
    {
        using var store = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        var database = QueueLifecycleRecoveryAssertions.Open(store);
        var operation = OrleansSerialization.Deserialize<Command<DatabaseCommand>>(await File.ReadAllBytesAsync(
            Path.Combine(root, QueueOrderedRetryCrashProtocol.OperationFile), token));
        var delivery = OrleansSerialization.Deserialize<Delivery>(await File.ReadAllBytesAsync(
            Path.Combine(root, QueueOrderedRetryCrashProtocol.DeliveryFile), token));
        var payload = operation.Value!;
        var decisions = OrleansSerialization.Deserialize<QueueRetryDecisions>(payload.RetryDecisions.Span);
        await QueueOrderedRetryRecoveryChoice.AssertAsync(store, operation, decisions);
        var outcome = OutcomeStoreOracle.Read(store, operation);
        if (stage >= CommitStage.JournalFlushed)
        {
            await Assert.That(outcome).IsNotNull();
        }
        await QueueOrderedRetryRecoveryImage.AssertAsync(database, delivery, decisions.Items.Single(), outcome is not null);
        var receipt = database.Apply(operation).Get<CommitReceipt>();
        if (outcome is { } retained)
        {
            await MessagingRecoveryReceiptAssertions.AssertSameValueAsync(retained.Get<CommitReceipt>(), receipt);
        }
        await QueueOrderedRetryRecoveryImage.AssertAsync(database, delivery, decisions.Items.Single(), committed: true);
        await MessagingRecoveryReceiptAssertions.AssertSameValueAsync(receipt, database.Apply(operation).Get<CommitReceipt>());
        await MessagingRecoveryReceiptAssertions.AssertSameValueAsync(receipt, database.ResolveOutcome(operation).Get<CommitReceipt>());
        await QueueOrderedRetryRecoveryContinuation.RunAsync(root, database, store, operation, receipt, decisions.Items.Single().RetryAt, token);
    }
}
