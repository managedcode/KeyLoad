using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class QueueLifecycleRecoveryAssertions
{
    internal static async Task VerifyAsync(string root, CommitStage stage, CancellationToken token)
    {
        using var store = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        var database = Open(store);
        var operation = JsonDefaults.Deserialize<ReplicatedOperation>(await File.ReadAllBytesAsync(
            Path.Combine(root, QueueLifecycleCrashProtocol.OperationFile), token));
        var outcome = OutcomeStoreOracle.Read(store, operation);
        var committed = outcome is not null;
        if (stage >= CommitStage.JournalFlushed)
        { await Assert.That(committed).IsTrue(); }
        await QueueLifecycleRecoveryImage.AssertAsync(database, store, committed);
        var receipt = database.Apply(operation).Get<CommitReceipt>();
        if (committed)
        { await MessagingRecoveryReceiptAssertions.AssertSameNativeValueAsync(outcome!.Get<CommitReceipt>(), receipt); }
        await QueueLifecycleRecoveryImage.AssertAsync(database, store, true);
        await MessagingRecoveryReceiptAssertions.AssertSameNativeValueAsync(receipt, database.Apply(operation).Get<CommitReceipt>());
        await MessagingRecoveryReceiptAssertions.AssertSameNativeValueAsync(receipt, database.ResolveOutcome(operation).Get<CommitReceipt>());
        await QueueLifecycleRecoveryContinuation.RunAsync(root, database, store, operation, receipt, token);
    }

    internal static DatabaseEngine Open(ZoneTreeStore store) => new(store, new AuthorizationPolicy(),
        RecoveryExecutionOptions.DatabaseLimits(), RecoveryExecutionOptions.DueWork(), RecoveryExecutionOptions.EventSource(),
        RecoveryExecutionOptions.Messaging(), RecoveryExecutionOptions.GraphExecution(), RecoveryExecutionOptions.ChangeFeedExecution(),
        RecoveryExecutionOptions.BlobExecution(), RecoveryExecutionOptions.NativeClaimsExecution(), RecoveryExecutionOptions.TimeSeriesExecution(),
        RecoveryExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance);
}
