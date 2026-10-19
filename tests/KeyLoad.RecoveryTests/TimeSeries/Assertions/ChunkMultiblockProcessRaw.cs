using KeyLoad.Core;

namespace KeyLoad.RecoveryTests.Features.TimeSeries;

internal static class ChunkMultiblockProcessRaw
{
    private const string Samples = "sample";
    private const string Identities = "sample-id";
    private const string Sequence = "sample-sequence";

    internal static async Task RequireAsync(ChunkMultiblockCrashSnapshot actual, bool prepared, bool healthy)
    {
        var records = ChunkMultiblockCrashData.Expected(healthy, prepared, merge: false);
        var points = records.Select(record => Pair(KeySpace.Partition(Samples, SampleChunkCrashContract.Partition,
            SampleChunkCrashContract.Set, SampleChunkCrashContract.Series, record.Sample.Timestamp, record.Sequence),
            OrleansSerialization.Serialize(record))).OrderBy(row => row[0], StringComparer.Ordinal);
        var identities = records.Select(record => Pair(KeySpace.Partition(Identities, SampleChunkCrashContract.Partition,
            SampleChunkCrashContract.Set, SampleChunkCrashContract.Series, record.Sample.EventId),
            OrleansSerialization.Serialize(JsonData.Fingerprint(new
            {
                sample = record.Sample,
                Tags = SampleChunkCrashContract.Tags
            }))))
            .OrderBy(row => row[0], StringComparer.Ordinal);
        var sequence = prepared ? ChunkMultiblockCrashProtocol.PreparedAutomaticSealSequence
            : healthy ? ChunkMultiblockCrashProtocol.HealthySequence : ChunkMultiblockCrashProtocol.PreparedSequence;
        string[][] expected = [.. points, .. identities, Pair(KeySpace.Partition(Sequence, SampleChunkCrashContract.Partition,
            SampleChunkCrashContract.Set, SampleChunkCrashContract.Series), OrleansSerialization.Serialize(sequence))];
        await Assert.That(actual.RawImage).IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected)));
    }

    private static string[] Pair(byte[] key, byte[] value) => [Convert.ToHexString(key), Convert.ToHexString(value)];
}
