using KeyLoad.Core;

namespace KeyLoad.CrashHost;

internal static class SampleChunkCrashRecovery
{
    internal static async Task RecoverAsync(string root, DatabaseEngine database, bool merge)
    {
        var outcome = await ReplayOriginalAsync(root, database).ConfigureAwait(false);
        await SampleChunkCrashState.WriteAsync(root, SampleChunkCrashContract.RecoveredFile, database, outcome)
            .ConfigureAwait(false);
        var acknowledged = Convert.ToHexString(NativeSerialization.Serialize(
            SampleChunkCrashOperations.OriginalAcknowledged(database)));
        var receiptPath = Path.Combine(root, SampleChunkCrashContract.OriginalReceiptFile);
        if (new FileInfo(receiptPath).Length > database.Limits.MaxBatchBytes)
        { throw new InvalidOperationException(SampleChunkCrashContract.Invalid); }
        if (acknowledged != Convert.ToHexString(await File.ReadAllBytesAsync(receiptPath).ConfigureAwait(false)))
        { throw new InvalidOperationException(SampleChunkCrashContract.Invalid); }
        if (!merge)
        {
            SampleChunkCrashOperations.Commit(database, Guid.NewGuid(), new AppendSamples(SampleChunkCrashContract.Set,
                SampleChunkCrashContract.Series, [SampleChunkCrashContract.Late], SampleChunkCrashContract.Tags))
                .Get<CommitReceipt>();
            SampleChunkCrashOperations.Commit(database, Guid.NewGuid(), SampleChunkCrashOperations.Intended(true))
                .Get<CommitReceipt>();
        }
        await SampleChunkCrashState.WriteAsync(root, SampleChunkCrashContract.HealthyFile, database, outcome)
            .ConfigureAwait(false);
    }

    internal static async Task VerifyAsync(string root, DatabaseEngine database)
    {
        var outcome = await ReplayOriginalAsync(root, database).ConfigureAwait(false);
        await SampleChunkCrashState.WriteAsync(root, SampleChunkCrashContract.FinalFile, database, outcome)
            .ConfigureAwait(false);
    }

    private static async Task<string> ReplayOriginalAsync(string root, DatabaseEngine database)
    {
        var path = Path.Combine(root, SampleChunkCrashContract.OriginalOperationFile);
        if (new FileInfo(path).Length > database.Limits.MaxBatchBytes)
        { throw new InvalidOperationException(SampleChunkCrashContract.Invalid); }
        var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        var original = NativeSerialization.Deserialize<ReplicatedOperation>(bytes);
        var before = SampleChunkCrashState.Complete(database);
        var position = database.Store.Position;
        var replay = database.ApplyEmbedded(original, cancellationToken: default);
        replay.Get<CommitReceipt>();
        if (SampleChunkCrashState.Complete(database) != before || database.Store.Position != position)
        { throw new InvalidOperationException(SampleChunkCrashContract.Invalid); }
        return Convert.ToHexString(NativeSerialization.Serialize(replay));
    }
}
