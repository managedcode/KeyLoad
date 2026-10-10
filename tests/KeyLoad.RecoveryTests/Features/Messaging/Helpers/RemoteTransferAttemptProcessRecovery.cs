using KeyLoad.CrashHost.Features.Messaging;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class RemoteTransferAttemptProcessRecovery
{
    internal static async Task VerifyAsync(string root, CommitStage stage, CancellationToken token)
    {
        var operation = JsonDefaults.Deserialize<ReplicatedOperation>(await File.ReadAllBytesAsync(Path.Combine(root, RemoteTransferAttemptCrashProtocol.OperationFile), token));
        var advance = JsonDefaults.Deserialize<CommandRequest>(System.Text.Encoding.UTF8.GetBytes(operation.PayloadJson));
        var failedAccept = JsonDefaults.Deserialize<CommandRequest>(await File.ReadAllBytesAsync(Path.Combine(root, RemoteTransferAttemptCrashProtocol.AcceptFile), token));
        var failedBytes = await File.ReadAllBytesAsync(Path.Combine(root, RemoteTransferAttemptCrashProtocol.FailureFile), token);
        CommitReceipt? advanced = null;
        RemoteTransferAttemptRecovered? result = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var store = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var database = RemoteTransferAttemptRecoveryDatabase.Open(store);
                await RemoteTransferAttemptRecoveryCut.AssertAsync(store, database, advance, stage);
                advanced = database.ApplyEmbedded(operation, token).Get<CommitReceipt>();
                await RemoteTransferAttemptRecoveryCut.AssertAsync(store, database, advance, CommitStage.ApplyCompleted);
                result = await RemoteTransferAttemptRecoveryContinuation.CompleteAsync(store, database, advance, failedAccept, failedBytes, token);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        await RemoteTransferAttemptRecoveryContinuation.ColdAsync(root, operation, advanced!, failedAccept, failedBytes, result!, token);
    }
}
