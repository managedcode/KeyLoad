using System.Text.Json;
using KeyLoad.Core.Features.TimeSeries;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class ChunkMultiblockPagedRetentionRepresentation
{
    private const int FirstOrdinal = 0;
    private const int SecondOrdinal = 1;
    private const int BlockCount = 2;
    private const int FirstBlockRecords = 256;

    internal static byte[][] Capture(SampleChunkPartialRetentionFixture fixture, long generation)
        => [SampleChunkPartialRetentionAssertions.Bytes(fixture, SampleChunkKeys.Manifest(fixture.Partition,
                SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series, fixture.WindowId, generation)),
            SampleChunkPartialRetentionAssertions.Bytes(fixture, SampleChunkKeys.Block(fixture.Partition,
                SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series, fixture.WindowId, generation, FirstOrdinal)),
            SampleChunkPartialRetentionAssertions.Bytes(fixture, SampleChunkKeys.Block(fixture.Partition,
                SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series, fixture.WindowId, generation, SecondOrdinal))];

    internal static async Task UnchangedAsync(SampleChunkPartialRetentionFixture fixture, byte[][] before)
    {
        var actual = Capture(fixture, ChunkMultiblockBoundaryData.InitialGeneration);
        await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options)).IsEqualTo(JsonSerializer.Serialize(before, JsonDefaults.Options));
    }

    internal static async Task RecordsAsync(SampleChunkPartialRetentionFixture fixture, bool healthy)
    {
        var expected = ChunkMultiblockBoundaryData.Expected(healthy);
        var native = Capture(fixture, healthy ? ChunkMultiblockBoundaryData.HealthyGeneration : ChunkMultiblockBoundaryData.InitialGeneration);
        var records = new List<SampleRecord>();
        for (var ordinal = 0; ordinal < BlockCount; ordinal++)
        {
            var decoded = SampleChunkCodec.DecodeOrdered(native[ordinal + 1], UnitExecutionOptions.TimeSeriesExecution(), SampleChunkCodec.MaximumEncodedBytes);
            await Assert.That(decoded.Length).IsEqualTo(ordinal == FirstOrdinal ? FirstBlockRecords : expected.Length - FirstBlockRecords);
            records.AddRange(decoded);
        }
        await Assert.That(JsonSerializer.Serialize(records, JsonDefaults.Options)).IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(BitConverter.DoubleToInt64Bits(records[index].Sample.Value)).IsEqualTo(BitConverter.DoubleToInt64Bits(expected[index].Sample.Value));
        }
    }
}
