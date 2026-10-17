using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleChunkStoreFailureAssertions
{
    internal static async Task AssertUnchangedAndReadableAsync(SampleChunkStoreFixture fixture,
        SampleChunkStoreCanonicalState before)
    {
        var after = SampleChunkStoreFixture.CaptureCanonicalState(fixture.Database);
        var visible = fixture.Database.Database.ReadSamples(SampleChunkStoreFixture.Root,
            fixture.Database.Partition, SampleChunkStoreFixture.Set, SampleChunkStoreFixture.Series,
            SampleChunkStoreFixture.Floor, SampleChunkStoreFixture.EndExclusive);

        await SampleChunkStoreAssertions.CanonicalStateAsync(before, after);
        await Assert.That(visible.Length).IsEqualTo(6);
        await Assert.That(visible.Any(sample => sample.Sample.EventId == "chunk-old")).IsFalse();
        await Assert.That(fixture.Database.Database.ReadSampleRetention(SampleChunkStoreFixture.Root,
                new(fixture.Database.Partition, SampleChunkStoreFixture.Set, SampleChunkStoreFixture.Series)).Before)
            .IsEqualTo(SampleChunkStoreFixture.Floor);
    }
}
