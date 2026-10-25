using System.Text.Json;

namespace KeyLoad.FunctionalTests.TimeSeries;

internal static class ChunkMultiblockBoundaryRows
{
    internal static async Task RequireAsync(ChunkSelectedIntegrityFixture fixture, bool healthy)
    {
        var cut = SampleRawIntegrityAssertions.Capture(fixture.Store);
        var expected = ChunkMultiblockBoundaryData.Expected(healthy);
        var chunk = fixture.Owner.ReadSampleChunkWindow(SampleChunkCanonicalFixture.Principal,
            new(fixture.Partition, SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
                fixture.WindowId, null, null, ChunkMultiblockBoundaryData.HealthyCount));
        var model = new SampleChunkWindowResult(fixture.WindowId, SampleChunkCanonicalFixture.Start,
            SampleChunkCanonicalFixture.Until, healthy ? ChunkMultiblockBoundaryData.HealthyGeneration : ChunkMultiblockBoundaryData.InitialGeneration,
            healthy ? ChunkMultiblockBoundaryData.HealthyRevision : ChunkMultiblockBoundaryData.InitialRevision,
            healthy ? ChunkMultiblockBoundaryData.HealthyCount : ChunkMultiblockBoundaryData.InitialCount,
            null, [.. expected], fixture.Store.Position);
        await Assert.That(JsonSerializer.Serialize(chunk, JsonDefaults.Options)).IsEqualTo(JsonSerializer.Serialize(model, JsonDefaults.Options));
        await RowsAsync(chunk.Records.ToArray(), expected);
        var raw = fixture.Owner.ReadSamples(SampleChunkCanonicalFixture.Principal, fixture.Partition,
            SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series, SampleChunkCanonicalFixture.Start,
            SampleChunkCanonicalFixture.Until, ChunkMultiblockBoundaryData.HealthyCount);
        await RowsAsync(raw, expected);
        await ChunkMultiblockBoundaryAggregates.RequireAsync(fixture, healthy, expected);
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, cut);
    }

    private static async Task RowsAsync(SampleRecord[] actual, SampleRecord[] expected)
    {
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(actual[index]).IsEqualTo(expected[index]);
            await Assert.That(actual[index].Sample.Timestamp.Ticks).IsEqualTo(expected[index].Sample.Timestamp.Ticks);
            await Assert.That(actual[index].Sample.Timestamp.Offset).IsEqualTo(expected[index].Sample.Timestamp.Offset);
            await Assert.That(BitConverter.DoubleToInt64Bits(actual[index].Sample.Value))
                .IsEqualTo(BitConverter.DoubleToInt64Bits(expected[index].Sample.Value));
        }
    }
}
