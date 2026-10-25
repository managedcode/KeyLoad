using KeyLoad.Core;
using KeyLoad.Core.TimeSeries;
using ManagedCode.Communication.Commands;

namespace KeyLoad.RecoveryTests;

internal static class SampleChunkEarlyCrashState
{
    internal static async Task WriteAsync(string root, string name, DatabaseEngine database, string? outcome = null)
    {
        var path = Path.Combine(root, SampleChunkCrashContract.OriginalReceiptFile);
        var acknowledged = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        var exists = database.Store.Read(view => view.ReadValue(SampleChunkKeys.Window(SampleChunkCrashContract.Partition,
            SampleChunkCrashContract.Set, SampleChunkCrashContract.Series, SampleChunkCrashContract.WindowId), static _ => { }));
        var window = exists ? database.ReadSampleChunkWindow(CrashFixtureValues.Principal,
            new(SampleChunkCrashContract.Partition, SampleChunkCrashContract.Set, SampleChunkCrashContract.Series,
                SampleChunkCrashContract.WindowId, null, null, SampleChunkCrashContract.SampleLimit)) : null;
        var rows = database.ReadSamples(CrashFixtureValues.Principal, SampleChunkCrashContract.Partition,
            SampleChunkCrashContract.Set, SampleChunkCrashContract.Series, SampleChunkCrashContract.From,
            SampleChunkCrashContract.Until, SampleChunkCrashContract.SampleLimit);
        var snapshot = new SampleChunkEarlyCrashSnapshot(window, rows, SampleChunkCrashState.Raw(database),
            database.Store.Position, Convert.ToHexString(acknowledged), outcome);
        await SampleChunkSnapshotFile.WriteAsync(Path.Combine(root, name), snapshot)
            .ConfigureAwait(false);
    }

    internal static async Task<string> ReplayAsync(string root, DatabaseEngine database)
    {
        var path = Path.Combine(root, SampleChunkCrashContract.OriginalOperationFile);
        var operation = OrleansSerialization.Deserialize<Command<DatabaseCommand>>(
            await File.ReadAllBytesAsync(path).ConfigureAwait(false));
        var before = SampleChunkCrashState.Complete(database);
        var position = database.Store.Position;
        var result = database.ApplyEmbedded(operation, cancellationToken: default);
        result.Get<CommitReceipt>();
        if (before != SampleChunkCrashState.Complete(database) || position != database.Store.Position)
        {
            throw new InvalidOperationException(SampleChunkCrashContract.Invalid);
        }
        var original = Convert.ToHexString(OrleansSerialization.Serialize(SampleChunkEarlyCrashOperations.Configure(database)));
        var receiptPath = Path.Combine(root, SampleChunkCrashContract.OriginalReceiptFile);
        if (original != Convert.ToHexString(await File.ReadAllBytesAsync(receiptPath).ConfigureAwait(false))
            || before != SampleChunkCrashState.Complete(database) || position != database.Store.Position)
        {
            throw new InvalidOperationException(SampleChunkCrashContract.Invalid);
        }
        return Convert.ToHexString(OrleansSerialization.Serialize(result));
    }
}
