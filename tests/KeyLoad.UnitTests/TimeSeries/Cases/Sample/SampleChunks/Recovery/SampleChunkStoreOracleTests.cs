using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleChunkStoreOracleTests
{
    [Test]
    public async Task AcChunk002CodecRoundTripMatchesPersistedCutAndAllFourRawReaders()
    {
        using var fixture = SampleChunkStoreFixture.Create();
        var database = fixture.Database;
        var cut = fixture.CaptureCodecCut();
        var canonicalAfter = SampleChunkStoreFixture.CaptureCanonicalState(database);
        var authority = SampleChunkStoreFixture.CaptureSeriesAuthority(database);

        await SampleChunkStoreAssertions.RecordsAsync(cut.Source, cut.Decoded);
        await Assert.That(cut.Source.Length).IsEqualTo(6);
        await Assert.That(cut.Source.Any(sample => sample.Sample.EventId == "chunk-old")).IsFalse();
        await Assert.That(cut.Source.Count(sample => sample.Sample.EventId == "chunk-b")).IsEqualTo(1);
        await Assert.That(cut.Source.Count(sample =>
            sample.Sample.Timestamp.UtcTicks == SampleChunkStoreFixture.FirstEqualUtc.UtcTicks)).IsEqualTo(3);
        await Assert.That(cut.Source.Select(sample => sample.Sequence)).IsEquivalentTo(
            new long[] { 5, 2, 3, 6, 4, 7 }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(authority.Any(record => record.Key.Span.SequenceEqual(
            SampleChunkStoreFixture.SampleIdKey(database, "chunk-old")))).IsTrue();
        await Assert.That(database.Database.ReadSampleRetention(SampleChunkStoreFixture.Root,
            new(database.Partition, SampleChunkStoreFixture.Set, SampleChunkStoreFixture.Series)).Before)
            .IsEqualTo(SampleChunkStoreFixture.Floor);
        await SampleChunkStoreOracleAssertions.ReaderResultsMatchRawOracleAsync(cut.Decoded, cut.Readers);
        await SampleChunkStoreAssertions.CanonicalStateAsync(cut.CanonicalState, canonicalAfter);
    }

    [Test]
    public async Task AcChunk002ChangedEventContentConflictsWithoutChangingSeriesAuthority()
    {
        using var fixture = SampleChunkStoreFixture.Create();
        var database = fixture.Database;
        var before = SampleChunkStoreFixture.CaptureSeriesAuthority(database);
        var changed = new AppendSamples(SampleChunkStoreFixture.Set, SampleChunkStoreFixture.Series,
            [new("chunk-old", SampleChunkStoreFixture.Floor.AddMinutes(-1), 99d)],
            SampleChunkStoreFixture.Tags);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => database.Commit(changed));
        var after = SampleChunkStoreFixture.CaptureSeriesAuthority(database);

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Conflict);
        await SampleChunkStoreAssertions.CanonicalRecordsAsync(before, after);
    }
}
