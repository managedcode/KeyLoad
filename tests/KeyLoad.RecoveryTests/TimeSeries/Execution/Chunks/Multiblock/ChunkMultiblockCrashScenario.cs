using System.Globalization;
using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

internal static class ChunkMultiblockCrashScenario
{
    internal static async Task<bool> TryRunAsync(string[] args)
    {
        if (args.Length != ChunkMultiblockCrashProtocol.Arguments || !ChunkMultiblockCrashProtocol.IsMode(args[ChunkMultiblockCrashProtocol.ModeArgument]))
        {
            return false;
        }
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var boundary = new CanonicalCrashBoundary(Enum.Parse<CommitStage>(args[ChunkMultiblockCrashProtocol.StageArgument]),
                int.Parse(args[ChunkMultiblockCrashProtocol.MutationArgument], CultureInfo.InvariantCulture), false);
            using var store = new ZoneTreeStore(new(args[ChunkMultiblockCrashProtocol.DirectoryArgument])
            {
                FaultObserver = boundary.Observe
            },
                CrashExecutionOptions.StorageExecution(), CrashExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(() => RunAsync(args, store, boundary), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return true;
    }

    private static async Task RunAsync(string[] args, ZoneTreeStore store, CanonicalCrashBoundary boundary)
    {
        var root = args[ChunkMultiblockCrashProtocol.DirectoryArgument];
        var mode = args[ChunkMultiblockCrashProtocol.ModeArgument];
        var merge = ChunkMultiblockCrashProtocol.IsMerge(mode);
        var database = ChunkMultiblockCrashProtocol.IsPrepare(mode) ? CrashDatabase.Create(store) : SampleChunkCrashScenario.OpenExisting(store);
        if (ChunkMultiblockCrashProtocol.IsPrepare(mode))
        {
            ChunkMultiblockCrashOperations.Seed(root, database, merge);
            await File.WriteAllBytesAsync(Path.Combine(root, SampleChunkCrashContract.OriginalReceiptFile),
                NativeSerialization.Serialize(SampleChunkCrashOperations.OriginalAcknowledged(database))).ConfigureAwait(false);
            await ChunkMultiblockCrashState.WriteAsync(root, SampleChunkCrashContract.PreparedFile, database, merge).ConfigureAwait(false);
            await RecoveryProcessPause.WaitForKillAsync();
        }
        else if (ChunkMultiblockCrashProtocol.IsFault(mode))
        {
            await FaultAsync(root, database, boundary, merge).ConfigureAwait(false);
        }
        else if (ChunkMultiblockCrashProtocol.IsVerify(mode))
        {
            await ChunkMultiblockCrashRecovery.VerifyAsync(root, database, mode, merge).ConfigureAwait(false);
        }
        else
        {
            await ChunkMultiblockCrashRecovery.RecoverAsync(root, database, merge).ConfigureAwait(false);
        }
    }

    private static async Task FaultAsync(string root, DatabaseEngine database, CanonicalCrashBoundary boundary, bool merge)
    {
        var request = new CommandRequest(SampleChunkCrashContract.InflightId, SampleChunkCrashContract.Partition,
            [ChunkMultiblockCrashOperations.Intended(merge)]);
        var original = CrashDatabase.Operation(database, OperationKind.Batch, request, request.CommandId);
        await File.WriteAllBytesAsync(Path.Combine(root, SampleChunkCrashContract.OriginalOperationFile), NativeSerialization.Serialize(original)).ConfigureAwait(false);
        SampleChunkSnapshotFile.CaptureExpectedToken(root, request.CommandId, database, request.Partition);
        boundary.Position = checked(database.Store.Position + SampleChunkCrashContract.PositionStep);
        boundary.Armed = true;
        database.ApplyEmbedded(original, cancellationToken: default).Get<CommitReceipt>();
        await RecoveryProcessPause.WaitForKillAsync();
    }
}
