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

internal static class SampleChunkStoreOracleAssertions
{
    internal static async Task ReaderResultsMatchRawOracleAsync(SampleRecord[] decoded,
        SampleChunkStoreReaderResults actual)
    {
        var ordered = decoded.OrderBy(record => record.Sample.Timestamp.UtcTicks)
            .ThenBy(record => record.Sequence).ToArray();
        var range = ordered.Where(record => record.Sample.Timestamp.UtcTicks >= SampleChunkStoreFixture.Floor.UtcTicks
            && record.Sample.Timestamp.UtcTicks <= SampleChunkStoreFixture.EndExclusive.UtcTicks).ToArray();
        await SampleChunkStoreAssertions.RecordsAsync(range, actual.Range);
        var latest = range.LastOrDefault();
        if (latest is null)
        {
            await Assert.That(actual.Latest).IsNull();
        }
        else
        {
            await SampleChunkStoreAssertions.RecordAsync(latest, actual.Latest!);
        }

        var aggregateRange = SelectRange(ordered, SampleChunkStoreFixture.Floor,
            SampleChunkStoreFixture.EndExclusive);
        await SampleChunkStoreAssertions.AggregateAsync(SampleAggregateTestData.Oracle(aggregateRange), actual.Aggregate);
        await SampleChunkStoreAssertions.WindowsAsync(ExpectedWindows(ordered), actual.Windows);
    }

    private static SampleRecord[] SelectRange(SampleRecord[] records, DateTimeOffset from, DateTimeOffset until)
        => records.Where(record => record.Sample.Timestamp.UtcTicks >= from.UtcTicks
            && record.Sample.Timestamp.UtcTicks < until.UtcTicks).ToArray();

    private static SampleAggregateWindow[] ExpectedWindows(SampleRecord[] ordered)
    {
        var windows = new SampleAggregateWindow[SampleChunkStoreFixture.WindowLimit];
        for (var index = 0; index < windows.Length; index++)
        {
            var from = SampleChunkStoreFixture.Floor.AddTicks(SampleChunkStoreFixture.WindowWidth.Ticks * index);
            var until = from.Add(SampleChunkStoreFixture.WindowWidth);
            windows[index] = new(from, until, SampleAggregateTestData.Oracle(SelectRange(ordered, from, until)));
        }

        return windows;
    }
}
