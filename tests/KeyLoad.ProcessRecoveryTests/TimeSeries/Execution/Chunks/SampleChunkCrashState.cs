using KeyLoad.Core;

namespace KeyLoad.ProcessRecoveryTests;

internal static class SampleChunkCrashState
{
    private const string Samples = "sample";
    private const string SampleIdentities = "sample-id";
    private const string SampleSequence = "sample-sequence";
    private const string SampleRetention = "sample-retention";
    private static readonly string[] RawFamilies = [Samples, SampleIdentities, SampleSequence, SampleRetention];

    internal static async Task WriteAsync(string root, string name, DatabaseEngine database,
        string? inflightResult = null)
    {
        var receiptPath = Path.Combine(root, SampleChunkCrashContract.OriginalReceiptFile);
        var receipt = await File.ReadAllBytesAsync(receiptPath).ConfigureAwait(false);
        var window = database.ReadSampleChunkWindow(CrashFixtureValues.Principal,
            new(SampleChunkCrashContract.Partition, SampleChunkCrashContract.Set, SampleChunkCrashContract.Series,
                SampleChunkCrashContract.WindowId, null, null, SampleChunkCrashContract.SampleLimit));
        var rows = database.ReadSamples(CrashFixtureValues.Principal, SampleChunkCrashContract.Partition,
            SampleChunkCrashContract.Set, SampleChunkCrashContract.Series, SampleChunkCrashContract.From,
            SampleChunkCrashContract.Until, SampleChunkCrashContract.SampleLimit);
        var state = new SampleChunkCrashSnapshot(window, rows, Raw(database), database.Store.Position,
            Convert.ToHexString(receipt), inflightResult);
        await SampleChunkSnapshotFile.WriteAsync(Path.Combine(root, name), state)
            .ConfigureAwait(false);
    }

    internal static string Raw(DatabaseEngine database) => database.Store.Read(view =>
    {
        var rows = new List<string[]>();
        foreach (var family in RawFamilies)
        {
            var prefix = KeySpace.Partition(family, SampleChunkCrashContract.Partition,
                SampleChunkCrashContract.Set, SampleChunkCrashContract.Series);
            var page = view.Scan(prefix, SampleChunkCrashContract.MaximumRecords);
            if (page.HasMore)
            {
                throw new InvalidOperationException(SampleChunkCrashContract.Invalid);
            }
            rows.AddRange(page.Records.Select(row =>
                new[] { Convert.ToHexString(row.Key.Span), Convert.ToHexString(row.Value.Span) }));
        }
        return Convert.ToHexString(JsonDefaults.Serialize(rows));
    });

    internal static string Complete(DatabaseEngine database) => database.Store.Read(view =>
    {
        var page = view.Scan([], SampleChunkCrashContract.MaximumRecords);
        if (page.HasMore)
        {
            throw new InvalidOperationException(SampleChunkCrashContract.Invalid);
        }
        return Convert.ToHexString(JsonDefaults.Serialize(page.Records));
    });
}
