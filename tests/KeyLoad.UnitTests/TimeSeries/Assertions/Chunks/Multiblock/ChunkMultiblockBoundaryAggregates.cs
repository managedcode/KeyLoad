namespace KeyLoad.UnitTests.TimeSeries;

internal static class ChunkMultiblockBoundaryAggregates
{
    private const double InitialSum = 514;
    private const double HealthySum = 522;
    private const double InitialMaximum = 4;
    private const double HealthyMaximum = 8;
    private const double InitialAverage = 2;
    private const double Minimum = -0d;

    internal static async Task RequireAsync(ChunkSelectedIntegrityFixture fixture, bool healthy, SampleRecord[] expected)
    {
        var latest = TimeSeriesReadOperations.ReadLatestSample(fixture.Owner, SampleChunkCanonicalFixture.Principal,
            new(fixture.Partition, SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series, SampleChunkCanonicalFixture.Until));
        await Assert.That(latest.Sample).IsEqualTo(expected[^1]);
        var aggregate = healthy
            ? new SampleAggregate(ChunkMultiblockBoundaryData.HealthyCount, HealthySum, Minimum, HealthyMaximum, HealthySum / ChunkMultiblockBoundaryData.HealthyCount)
            : new SampleAggregate(ChunkMultiblockBoundaryData.InitialCount, InitialSum, Minimum, InitialMaximum, InitialAverage);
        await Assert.That(TimeSeriesReadOperations.AggregateSamples(fixture.Owner, SampleChunkCanonicalFixture.Principal,
            new(fixture.Partition, SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
                SampleChunkCanonicalFixture.Start, SampleChunkCanonicalFixture.Until))).IsEqualTo(aggregate);
        var windows = TimeSeriesReadOperations.AggregateSampleWindows(fixture.Owner, SampleChunkCanonicalFixture.Principal,
            new(fixture.Partition, SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
                SampleChunkCanonicalFixture.Start, SampleChunkCanonicalFixture.Until, TimeSpan.FromHours(1)));
        await Assert.That(windows.Windows.Length).IsEqualTo(1);
        await Assert.That(windows.Windows[0]).IsEqualTo(new SampleAggregateWindow(
            SampleChunkCanonicalFixture.Start, SampleChunkCanonicalFixture.Until, aggregate));
        var unrelated = fixture.Owner.ReadSamples(SampleChunkCanonicalFixture.Principal, fixture.Partition,
            SampleChunkCanonicalFixture.Set, ChunkSelectedIntegrityFixture.OtherSeries, SampleChunkCanonicalFixture.Start,
            SampleChunkCanonicalFixture.Until, SampleChunkCanonicalFixture.OutputLimit);
        await Assert.That(unrelated).IsEquivalentTo(new SampleRecord[] { new(ChunkSelectedIntegrityFixture.OtherSeries,
            ChunkSelectedIntegrityFixture.Other, 1L, SampleChunkCanonicalFixture.Tags) }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}
