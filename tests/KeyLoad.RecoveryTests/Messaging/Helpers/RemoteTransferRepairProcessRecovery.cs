using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.Messaging;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class RemoteTransferRepairProcessRecovery
{
    internal static async Task VerifyAsync(string root, CommitStage stage, CancellationToken token)
    {
        var operation = JsonDefaults.Deserialize<ReplicatedOperation>(await File.ReadAllBytesAsync(Path.Combine(root, RemoteTransferRepairCrashProtocol.OperationFile), token));
        var command = JsonDefaults.Deserialize<CommandRequest>(System.Text.Encoding.UTF8.GetBytes(operation.PayloadJson));
        var failed = JsonDefaults.Deserialize<CommandRequest>(await File.ReadAllBytesAsync(Path.Combine(root, RemoteTransferRepairCrashProtocol.OriginalFile), token));
        var bytes = await File.ReadAllBytesAsync(Path.Combine(root, RemoteTransferRepairCrashProtocol.FailureFile), token);
        var failures = new List<Exception>();
        CommitReceipt? repaired = null;
        RemoteTransferRepairRecovered? result = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var store = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var database = RemoteTransferRepairRecoveryDatabase.Open(store);
                await RemoteTransferRepairRecoveryCut.AssertAsync(store, database, command, stage);
                repaired = database.ApplyEmbedded(operation, token).Get<CommitReceipt>();
                await RemoteTransferRepairRecoveryCut.AssertAsync(store, database, command, CommitStage.ApplyCompleted);
                result = await RemoteTransferRepairRecoveryContinuation.CompleteAsync(store, database, command, failed, bytes, token);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        await ColdAsync(root, operation, repaired!, failed, bytes, result!, token);
        await ColdAsync(root, operation, repaired!, failed, bytes, result!, token);
    }

    private static async Task ColdAsync(string root, ReplicatedOperation operation, CommitReceipt repaired,
        CommandRequest failed, byte[] bytes, RemoteTransferRepairRecovered result, CancellationToken token)
    {
        using var store = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        var database = RemoteTransferRepairRecoveryDatabase.Open(store);
        await RemoteTransferRepairRecoveryContinuation.EqualAsync(database.ApplyEmbedded(operation, token).Get<CommitReceipt>(), repaired);
        await RemoteTransferRepairRecoveryContinuation.EqualAsync(database.ApplyEmbedded(CrashDatabase.Operation(OperationKind.Batch,
            result.Complete, result.Complete.CommandId), token).Get<CommitReceipt>(), result.Completed);
        await RemoteTransferRepairRecoveryContinuation.EqualAsync(database.InspectQueueTransfer(CrashFixtureValues.Principal,
            RemoteTransferAttemptCrashScenario.Source, result.Proof.TransferId, token), result.Delivered);
        await RemoteTransferRepairRecoveryContinuation.EqualAsync(database.InspectQueueTransferReceipt(CrashFixtureValues.Principal,
            RemoteTransferAttemptCrashScenario.Destination, RemoteTransferAttemptCrashScenario.Source, result.Proof.TransferId, token), result.Proof);
        await RemoteTransferRepairRecoveryContinuation.EqualAsync(database.InspectMessage(CrashFixtureValues.Principal,
            RemoteTransferAttemptCrashScenario.Destination, RemoteTransferAttemptCrashProtocol.OriginalMessage), result.Message);
        await RemoteTransferRepairRecoveryContinuation.FailureAsync(store, database, failed, bytes, token);
    }
}
