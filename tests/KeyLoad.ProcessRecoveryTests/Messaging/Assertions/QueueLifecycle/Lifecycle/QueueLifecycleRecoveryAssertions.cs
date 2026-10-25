using KeyLoad.Core;
using KeyLoad.Core.Authorization;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.ProcessRecoveryTests.Messaging;

internal static class QueueLifecycleRecoveryAssertions
{
    internal static async Task VerifyAsync(string root, CommitStage stage, CancellationToken token)
    {
        using var store = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        var database = Open(store);
        var operation = JsonDefaults.Deserialize<Command<DatabaseCommand>>(await File.ReadAllBytesAsync(
            Path.Combine(root, QueueLifecycleCrashProtocol.OperationFile), token));
        var outcome = OutcomeStoreOracle.Read(store, operation);
        var committed = outcome is not null;
        if (stage >= CommitStage.JournalFlushed)
        {
            await Assert.That(committed).IsTrue();
        }
        await QueueLifecycleRecoveryImage.AssertAsync(database, store, committed);
        var receipt = database.Apply(operation).Get<CommitReceipt>();
        if (outcome is { } retained)
        {
            await MessagingRecoveryReceiptAssertions.AssertSameValueAsync(retained.Get<CommitReceipt>(), receipt);
        }
        await QueueLifecycleRecoveryImage.AssertAsync(database, store, true);
        await MessagingRecoveryReceiptAssertions.AssertSameValueAsync(receipt, database.Apply(operation).Get<CommitReceipt>());
        await MessagingRecoveryReceiptAssertions.AssertSameValueAsync(receipt, database.ResolveOutcome(operation).Get<CommitReceipt>());
        await QueueLifecycleRecoveryContinuation.RunAsync(root, database, store, operation, receipt, token);
    }

    internal static DatabaseEngine Open(ZoneTreeStore store) => new(store, new AuthorizationPolicy(),
        RecoveryExecutionOptions.DatabaseLimits(), RecoveryExecutionOptions.DueWork(), RecoveryExecutionOptions.EventSource(),
        RecoveryExecutionOptions.Messaging(), RecoveryExecutionOptions.GraphExecution(), RecoveryExecutionOptions.ChangeFeedExecution(),
        RecoveryExecutionOptions.RuntimeClaimsExecution(), RecoveryExecutionOptions.TimeSeriesExecution());
}
