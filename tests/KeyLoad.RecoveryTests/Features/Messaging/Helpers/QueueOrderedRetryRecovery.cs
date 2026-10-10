using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.CrashHost;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class QueueOrderedRetryRecovery
{
    internal static async Task VerifyAsync(string root, CommitStage stage, CancellationToken token)
    {
        using var store = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        var database = QueueLifecycleRecoveryAssertions.Open(store);
        var operation = NativeSerialization.Deserialize<ReplicatedOperation>(await File.ReadAllBytesAsync(
            Path.Combine(root, QueueOrderedRetryCrashProtocol.OperationFile), token));
        var delivery = NativeSerialization.Deserialize<Delivery>(await File.ReadAllBytesAsync(
            Path.Combine(root, QueueOrderedRetryCrashProtocol.DeliveryFile), token));
        var payload = NativeSerialization.Deserialize<NativeCommandPayload>(operation.NativePayload.Span);
        var decisions = NativeSerialization.Deserialize<QueueRetryDecisions>(payload.RetryDecisions.Span);
        await QueueOrderedRetryRecoveryChoice.AssertAsync(store, operation, payload, decisions);
        var outcome = OutcomeStoreOracle.Read(store, operation);
        if (stage >= CommitStage.JournalFlushed)
        { await Assert.That(outcome).IsNotNull(); }
        await QueueOrderedRetryRecoveryImage.AssertAsync(database, delivery, decisions.Items.Single(), outcome is not null);
        var receipt = database.Apply(operation).Get<CommitReceipt>();
        if (outcome is not null)
        { await MessagingRecoveryReceiptAssertions.AssertSameNativeValueAsync(outcome.Get<CommitReceipt>(), receipt); }
        await QueueOrderedRetryRecoveryImage.AssertAsync(database, delivery, decisions.Items.Single(), committed: true);
        await MessagingRecoveryReceiptAssertions.AssertSameNativeValueAsync(receipt, database.Apply(operation).Get<CommitReceipt>());
        await MessagingRecoveryReceiptAssertions.AssertSameNativeValueAsync(receipt, database.ResolveOutcome(operation).Get<CommitReceipt>());
        await QueueOrderedRetryRecoveryContinuation.RunAsync(root, database, store, operation, receipt, decisions.Items.Single().RetryAt, token);
    }
}
