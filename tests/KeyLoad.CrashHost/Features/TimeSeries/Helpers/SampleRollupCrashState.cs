using System.Text.Json;
using KeyLoad.Core;

namespace KeyLoad.CrashHost;

internal static class SampleRollupCrashState
{
    private const string SampleSpace = "sample";
    private const string IdentitySpace = "sample-id";
    private const string SequenceSpace = "sample-sequence";
    private const string RetentionSpace = "sample-retention-v1";

    internal static SampleRollupCrashSnapshot Capture(string directory, DatabaseEngine database)
        => new(database.ReadSamples(CrashFixtureValues.Principal, SampleRollupCrashContract.Partition,
                SampleRollupCrashContract.Set, SampleRollupCrashContract.Series,
                SampleRollupCrashContract.Start, SampleRollupCrashContract.End, SampleRollupCrashContract.SampleLimit),
            database.ReadSampleRollup(CrashFixtureValues.Principal, new(SampleRollupCrashContract.Partition,
                SampleRollupCrashContract.Set, SampleRollupCrashContract.Series,
                SampleRollupCrashContract.Start, SampleRollupCrashContract.End)),
            Convert.ToHexString(Raw(database)), database.Store.Position,
            Convert.ToHexString(File.ReadAllBytes(Path.Combine(directory, SampleRollupCrashContract.AcknowledgedReceiptFile))));

    internal static Task WriteAsync(string directory, string file, DatabaseEngine database)
        => File.WriteAllBytesAsync(Path.Combine(directory, file), JsonDefaults.Serialize(Capture(directory, database)));

    internal static byte[] Raw(DatabaseEngine database) => database.Store.Read(view =>
    {
        var records = new List<string[]>();
        foreach (var space in new[] { SampleSpace, IdentitySpace, SequenceSpace, RetentionSpace })
        {
            var prefix = KeySpace.Partition(space, SampleRollupCrashContract.Partition,
                SampleRollupCrashContract.Set, SampleRollupCrashContract.Series);
            var page = view.Scan(prefix, SampleRollupCrashContract.MaximumRecords);
            if (page.HasMore)
            { throw new InvalidOperationException(SampleRollupCrashContract.Invalid); }
            records.AddRange(page.Records.Select(record =>
                new[] { Convert.ToHexString(record.Key.Span), Convert.ToHexString(record.Value.Span) }));
        }
        return JsonSerializer.SerializeToUtf8Bytes(records, JsonDefaults.Options);
    });

    internal static byte[] All(DatabaseEngine database) => database.Store.Read(view =>
    {
        var page = view.Scan([], SampleRollupCrashContract.MaximumRecords);
        if (page.HasMore)
        { throw new InvalidOperationException(SampleRollupCrashContract.Invalid); }
        return JsonDefaults.Serialize(page.Records);
    });
}
