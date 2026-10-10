using KeyLoad.CrashHost;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class QueueDeadlineRecoveryAssertions
{
    internal static async Task VerifyAsync(string root, CommitStage stage, CancellationToken token)
    {
        using var store = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        var database = QueueLifecycleRecoveryAssertions.Open(store);
        var operation = JsonDefaults.Deserialize<ReplicatedOperation>(await File.ReadAllBytesAsync(
            Path.Combine(root, QueueDeadlineCrashProtocol.OperationFile), token));
        var original = JsonDefaults.Deserialize<CommandRequest>(operation.PayloadJson);
        var condition = (AdvanceQueueDeadline)original.Mutations.Single();
        var outcome = OutcomeStoreOracle.Read(store, operation);
        var committed = outcome is not null;
        if (stage >= CommitStage.JournalFlushed)
        { await Assert.That(committed).IsTrue(); }
        await QueueDeadlineRecoveryImage.RequireAsync(database, committed, condition.ExpectedDeadline);
        var receipt = database.Apply(operation).Get<CommitReceipt>();
        if (committed)
        { await MessagingRecoveryReceiptAssertions.AssertSameNativeValueAsync(outcome!.Get<CommitReceipt>(), receipt); }
        await QueueDeadlineRecoveryImage.RequireAsync(database, true, condition.ExpectedDeadline);
        var expectedReceipt = new MutationReceipt(QueueDeadlineCrashProtocol.ReceiptKind,
            QueueDeadlineCrashProtocol.Queue, QueueDeadlineCrashProtocol.Message, QueueDeadlineCrashProtocol.Second);
        await Assert.That(NativeSerialization.Serialize(receipt.Mutations.Single()).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(expectedReceipt))).IsTrue();
        await MessagingRecoveryReceiptAssertions.AssertSameNativeValueAsync(receipt, database.Apply(operation).Get<CommitReceipt>());
        await MessagingRecoveryReceiptAssertions.AssertSameNativeValueAsync(receipt, database.ResolveOutcome(operation).Get<CommitReceipt>());
        store.Dispose();
        token.ThrowIfCancellationRequested();
        using var coldStore = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        var cold = QueueLifecycleRecoveryAssertions.Open(coldStore);
        await MessagingRecoveryReceiptAssertions.AssertSameNativeValueAsync(receipt, cold.Apply(operation).Get<CommitReceipt>());
        await QueueDeadlineRecoveryImage.RequireAsync(cold, true, condition.ExpectedDeadline);
        await QueueDeadlineRecoveryHealthy.RunAsync(cold);
    }
}
