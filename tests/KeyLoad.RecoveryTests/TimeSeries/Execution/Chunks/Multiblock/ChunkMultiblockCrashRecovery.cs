using KeyLoad.Core;
namespace KeyLoad.RecoveryTests;

internal static class ChunkMultiblockCrashRecovery
{
    internal static async Task RecoverAsync(string root, DatabaseEngine database, bool merge)
    {
        var inflight = await SampleChunkCrashRecovery.ReplayOriginalForMultiblockAsync(root, database).ConfigureAwait(false);
        await ChunkMultiblockCrashState.WriteAsync(root, SampleChunkCrashContract.RecoveredFile, database, merge, inflight).ConfigureAwait(false);
        await RequireAcknowledgedAsync(root, database).ConfigureAwait(false);
        SampleChunkSnapshotFile.CaptureExpectedToken(root, ChunkMultiblockCrashData.FreshCommandId, database, SampleChunkCrashContract.Partition);
        var appended = ChunkMultiblockCrashOperations.Fresh(database);
        appended.Get<CommitReceipt>();
        SampleChunkSnapshotFile.CaptureExpectedToken(root, ChunkMultiblockCrashData.HealthyMergeId, database, SampleChunkCrashContract.Partition);
        var merged = ChunkMultiblockCrashOperations.HealthyMerge(database, merge);
        merged.Get<CommitReceipt>();
        await ChunkMultiblockCrashState.WriteAsync(root, SampleChunkCrashContract.HealthyFile, database, merge, inflight,
            Convert.ToHexString(NativeSerialization.Serialize(appended)), Convert.ToHexString(NativeSerialization.Serialize(merged))).ConfigureAwait(false);
    }

    internal static async Task VerifyAsync(string root, DatabaseEngine database, string mode, bool merge)
    {
        var inflight = await SampleChunkCrashRecovery.ReplayOriginalForMultiblockAsync(root, database).ConfigureAwait(false);
        await RequireAcknowledgedAsync(root, database).ConfigureAwait(false);
        var appended = ChunkMultiblockCrashState.ReplayUnchanged(database, () => ChunkMultiblockCrashOperations.Fresh(database));
        var merged = ChunkMultiblockCrashState.ReplayUnchanged(database, () => ChunkMultiblockCrashOperations.HealthyMerge(database, merge));
        var name = ChunkMultiblockCrashProtocol.IsFirstCold(mode) ? ChunkMultiblockCrashProtocol.FirstColdFile : ChunkMultiblockCrashProtocol.SecondColdFile;
        await ChunkMultiblockCrashState.WriteAsync(root, name, database, merge, inflight, appended, merged).ConfigureAwait(false);
    }
    private static async Task RequireAcknowledgedAsync(string root, DatabaseEngine database)
    {
        var before = SampleChunkCrashState.Complete(database);
        var position = database.Store.Position;
        var acknowledged = Convert.ToHexString(NativeSerialization.Serialize(SampleChunkCrashOperations.OriginalAcknowledged(database)));
        var path = Path.Combine(root, SampleChunkCrashContract.OriginalReceiptFile);
        if (new FileInfo(path).Length > database.Limits.MaxBatchBytes)
        {
            throw new InvalidOperationException(SampleChunkCrashContract.Invalid);
        }
        var saved = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        if (acknowledged != Convert.ToHexString(saved) || SampleChunkCrashState.Complete(database) != before || database.Store.Position != position)
        {
            throw new InvalidOperationException(SampleChunkCrashContract.Invalid);
        }
    }
}
