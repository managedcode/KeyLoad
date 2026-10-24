namespace KeyLoad.UnitTests.TimeSeries;

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
