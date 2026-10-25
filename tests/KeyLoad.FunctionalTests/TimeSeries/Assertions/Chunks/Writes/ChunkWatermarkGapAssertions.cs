using KeyLoad.Core.TimeSeries;

namespace KeyLoad.FunctionalTests.TimeSeries;

internal static class ChunkWatermarkGapAssertions
{
    internal static async Task Healthy(ChunkSelectedIntegrityFixture fixture, bool second)
    {
        var cut = SampleRawIntegrityAssertions.Capture(fixture.Store);
        var actual = fixture.Owner.ReadSampleChunkWindow(SampleChunkCanonicalFixture.Principal,
            new(fixture.Partition, SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
                fixture.WindowId, null, null, SampleChunkCanonicalFixture.OutputLimit));
        var expected = new SampleChunkWindowResult(fixture.WindowId, SampleChunkCanonicalFixture.Start,
            SampleChunkCanonicalFixture.Until, 2, 6, 4, null,
            [.. SampleChunkCanonicalFixture.Expected(late: true)], cut.Position);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected)));
        var key = SampleChunkKeys.Manifest(fixture.Partition, SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, 2);
        var envelope = OrleansSerialization.Deserialize<SampleChunkManifestEnvelope>(
            fixture.Store.Read(view => view.ReadOwnedValue(key))!);
        var manifest = OrleansSerialization.Deserialize<SampleChunkManifest>(envelope.Encoded.Span);
        await Assert.That(manifest.SourceSequence).IsEqualTo(4);
        await Assert.That(manifest.RecordCount).IsEqualTo(3);
        await ChunkSelectedIntegrityAssertions.Raw(fixture, final: false);
        await Outside(fixture, second);
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, cut);
    }

    private static async Task Outside(ChunkSelectedIntegrityFixture fixture, bool second)
    {
        var actual = fixture.Owner.ReadSamples(SampleChunkCanonicalFixture.Principal, fixture.Partition,
            SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series, SampleChunkCanonicalFixture.Until,
            SampleChunkCanonicalFixture.Until.AddMinutes(1), SampleChunkCanonicalFixture.OutputLimit);
        SampleRecord first = new(SampleChunkCanonicalFixture.Series, ChunkWatermarkGapTrial.FirstOutside,
            4, SampleChunkCanonicalFixture.Tags);
        SampleRecord[] expected = second ? [first, new(SampleChunkCanonicalFixture.Series,
            ChunkWatermarkGapTrial.SecondOutside, 5, SampleChunkCanonicalFixture.Tags)] : [first];
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(OrleansSerialization.Serialize(actual[index])
                .SequenceEqual(OrleansSerialization.Serialize(expected[index]))).IsTrue();
        }
    }
}
