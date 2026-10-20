using System.Text.Json;
using KeyLoad.Core.Features.TimeSeries;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class ChunkMultiblockPagedRetentionRows
{
    private const string RawFamily = "sample";
    internal static async Task RequireAsync(SampleChunkPartialRetentionFixture fixture, bool expired, bool healthy)
    {
        var cut = SampleRawIntegrityAssertions.Capture(fixture.Store);
        var all = ChunkMultiblockBoundaryData.Expected(healthy);
        var expected = expired ? all.Where(row => row.Sample.Timestamp.UtcTicks >= SampleChunkCanonicalFixture.Late.Timestamp.UtcTicks).ToArray() : all;
        var actual = fixture.Owner.ReadSampleChunkWindow(SampleChunkCanonicalFixture.Principal,
            new(fixture.Partition, SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
                fixture.WindowId, null, null, ChunkMultiblockBoundaryData.HealthyCount));
        var model = new SampleChunkWindowResult(fixture.WindowId, SampleChunkCanonicalFixture.Start,
            SampleChunkCanonicalFixture.Until, healthy ? ChunkMultiblockBoundaryData.HealthyGeneration : ChunkMultiblockBoundaryData.InitialGeneration,
            healthy ? ChunkMultiblockBoundaryData.HealthyRevision : ChunkMultiblockBoundaryData.InitialRevision,
            healthy ? ChunkMultiblockBoundaryData.HealthyCount : ChunkMultiblockBoundaryData.InitialCount,
            expired ? SampleChunkCanonicalFixture.Late.Timestamp.UtcTicks : null, [.. expected], cut.Position);
        await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options)).IsEqualTo(JsonSerializer.Serialize(model, JsonDefaults.Options));
        var raw = fixture.Owner.ReadSamples(SampleChunkCanonicalFixture.Principal, fixture.Partition,
            SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series, SampleChunkCanonicalFixture.Start,
            SampleChunkCanonicalFixture.Until, ChunkMultiblockBoundaryData.HealthyCount);
        await Assert.That(JsonSerializer.Serialize(raw, JsonDefaults.Options)).IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(raw[index].Sample.Timestamp.Ticks).IsEqualTo(expected[index].Sample.Timestamp.Ticks);
            await Assert.That(raw[index].Sample.Timestamp.Offset).IsEqualTo(expected[index].Sample.Timestamp.Offset);
            await Assert.That(BitConverter.DoubleToInt64Bits(raw[index].Sample.Value)).IsEqualTo(BitConverter.DoubleToInt64Bits(expected[index].Sample.Value));
        }
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, cut);
    }

    internal static async Task PageAsync(SampleChunkPartialRetentionFixture fixture, long purged, bool more)
    {
        var cut = SampleRawIntegrityAssertions.Capture(fixture.Store);
        var status = fixture.Owner.ReadSampleRetention(SampleChunkCanonicalFixture.Principal,
            new(fixture.Partition, SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series));
        await Assert.That(status).IsEqualTo(new SampleRetentionStatus(SampleChunkCanonicalFixture.Late.Timestamp, purged, more));
        var page = fixture.Store.Read(view => view.Scan(SampleReadKeys.Prefix(fixture.Partition,
            SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series), ChunkMultiblockBoundaryData.HealthyCount));
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Records.Length).IsEqualTo(checked((int)(ChunkMultiblockBoundaryData.InitialCount - purged)));
        var remaining = ChunkMultiblockBoundaryData.Expected(healthy: false).Skip(checked((int)purged)).ToArray();
        for (var index = 0; index < remaining.Length; index++)
        {
            var record = OrleansSerialization.Deserialize<SampleRecord>(page.Records[index].Value.Span);
            await Assert.That(JsonSerializer.Serialize(record, JsonDefaults.Options)).IsEqualTo(JsonSerializer.Serialize(remaining[index], JsonDefaults.Options));
            var key = KeySpace.Partition(RawFamily, fixture.Partition, SampleChunkCanonicalFixture.Set,
                SampleChunkCanonicalFixture.Series, remaining[index].Sample.Timestamp, remaining[index].Sequence);
            await Assert.That(page.Records[index].Key.Span.SequenceEqual(key)).IsTrue();
            await Assert.That(BitConverter.DoubleToInt64Bits(record.Sample.Value)).IsEqualTo(BitConverter.DoubleToInt64Bits(remaining[index].Sample.Value));
        }
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, cut);
    }
}
