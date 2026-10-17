using System.Security.Cryptography;
using KeyLoad.Core;

namespace KeyLoad.CrashHost;

internal static class ChunkMultiblockCrashState
{
    private const string BlockFamily = "sample-chunk-block";

    internal static async Task WriteAsync(string root, string name, DatabaseEngine database, bool merge,
        string? inflight = null, string? appended = null, string? merged = null)
    {
        var receiptPath = Path.Combine(root, SampleChunkCrashContract.OriginalReceiptFile);
        if (new FileInfo(receiptPath).Length > database.Limits.MaxBatchBytes)
        {
            throw new InvalidOperationException(SampleChunkCrashContract.Invalid);
        }
        var acknowledged = await File.ReadAllBytesAsync(receiptPath).ConfigureAwait(false);
        var window = database.ReadSampleChunkWindow(CrashFixtureValues.Principal,
            new(SampleChunkCrashContract.Partition, SampleChunkCrashContract.Set, SampleChunkCrashContract.Series,
                SampleChunkCrashContract.WindowId, null, null, ChunkMultiblockCrashProtocol.SampleLimit));
        var raw = database.ReadSamples(CrashFixtureValues.Principal, SampleChunkCrashContract.Partition,
            SampleChunkCrashContract.Set, SampleChunkCrashContract.Series, SampleChunkCrashContract.From,
            SampleChunkCrashContract.Until, ChunkMultiblockCrashProtocol.SampleLimit);
        var additional = ReplayUnchanged(database, () => ChunkMultiblockCrashOperations.Additional(database, merge));
        var digest = Convert.ToHexString(SHA256.HashData(Convert.FromHexString(SampleChunkCrashState.Complete(database))));
        var snapshot = new ChunkMultiblockCrashSnapshot(window, raw, SampleChunkCrashState.Raw(database),
            Blocks(database, window.Generation), digest, database.Store.Position, database.Store.Identity.Incarnation, database.Store.Identity.Durability, Convert.ToHexString(acknowledged),
            additional, inflight, appended, merged);
        await SampleChunkSnapshotFile.WriteAsync(Path.Combine(root, name), snapshot, database.Limits.MaxBatchBytes).ConfigureAwait(false);
    }

    internal static string ReplayUnchanged(DatabaseEngine database, Func<OperationResult> replay)
    {
        var before = SampleChunkCrashState.Complete(database);
        var position = database.Store.Position;
        var result = replay();
        result.Get<CommitReceipt>();
        if (database.Store.Position != position || SampleChunkCrashState.Complete(database) != before)
        {
            throw new InvalidOperationException(SampleChunkCrashContract.Invalid);
        }
        return Convert.ToHexString(NativeSerialization.Serialize(result));
    }

    private static string[][] Blocks(DatabaseEngine database, long generation)
    {
        if (generation == ChunkMultiblockCrashProtocol.OpenGeneration)
        {
            return [];
        }
        return database.Store.Read(view =>
        {
            var prefix = KeySpace.Partition(BlockFamily, SampleChunkCrashContract.Partition, SampleChunkCrashContract.Set,
                SampleChunkCrashContract.Series, SampleChunkCrashContract.WindowId, generation);
            var page = view.Scan(prefix, SampleChunkCrashContract.MaximumRecords);
            if (page.HasMore)
            {
                throw new InvalidOperationException(SampleChunkCrashContract.Invalid);
            }
            return page.Records.Select(record => new[] { Convert.ToHexString(record.Key.Span), Convert.ToHexString(record.Value.Span) }).ToArray();
        });
    }
}
