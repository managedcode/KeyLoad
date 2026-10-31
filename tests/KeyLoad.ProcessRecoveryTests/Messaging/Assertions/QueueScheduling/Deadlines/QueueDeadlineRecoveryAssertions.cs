using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.ProcessRecoveryTests.Messaging;

internal static class QueueDeadlineRecoveryAssertions
{
    internal static async Task VerifyAsync(string root, CommitStage stage, CancellationToken token)
    {
        using var store = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        var database = QueueLifecycleRecoveryAssertions.Open(store);
        var operation = OrleansSerialization.Deserialize<Command<DatabaseCommand>>(await File.ReadAllBytesAsync(
            Path.Combine(root, QueueDeadlineCrashProtocol.OperationFile), token));
        var original = ((CommandRequest)operation.Value!.Input);
        var condition = (AdvanceQueueDeadline)original.Mutations.Single();
        await Assert.That(new DateTimeOffset(operation.Timestamp) >= condition.ExpectedDeadline).IsTrue();
        var outcome = OutcomeStoreOracle.Read(store, operation);
        var committed = outcome is not null;
        if (stage >= CommitStage.JournalFlushed)
        {
            await Assert.That(committed).IsTrue();
        }
        await QueueDeadlineRecoveryImage.RequireAsync(database, committed, condition.ExpectedDeadline);
        var receipt = database.Apply(operation).Get<CommitReceipt>();
        if (outcome is { } retained)
        {
            await MessagingRecoveryReceiptAssertions.AssertSameValueAsync(retained.Get<CommitReceipt>(), receipt);
        }
        await QueueDeadlineRecoveryImage.RequireAsync(database, true, condition.ExpectedDeadline);
        var expectedReceipt = new MutationReceipt(QueueDeadlineCrashProtocol.ReceiptKind,
            QueueDeadlineCrashProtocol.Queue, QueueDeadlineCrashProtocol.Message, QueueDeadlineCrashProtocol.Second);
        await Assert.That(OrleansSerialization.Serialize(receipt.Mutations.Single()).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(expectedReceipt))).IsTrue();
        await MessagingRecoveryReceiptAssertions.AssertSameValueAsync(receipt, database.Apply(operation).Get<CommitReceipt>());
        await MessagingRecoveryReceiptAssertions.AssertSameValueAsync(receipt, database.ResolveOutcome(operation).Get<CommitReceipt>());
        store.Dispose();
        token.ThrowIfCancellationRequested();
        using var coldStore = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        var cold = QueueLifecycleRecoveryAssertions.Open(coldStore);
        await MessagingRecoveryReceiptAssertions.AssertSameValueAsync(receipt, cold.Apply(operation).Get<CommitReceipt>());
        await QueueDeadlineRecoveryImage.RequireAsync(cold, true, condition.ExpectedDeadline);
        await QueueDeadlineRecoveryHealthy.RunAsync(cold);
    }
}
