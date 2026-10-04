using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleLatestTests
{
    [Test]
    public async Task AcSeries008LatestUsesUtcTimestampAndGreatestSequenceAtInclusiveCut()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        var cutWithOffset = SampleAggregateTestData.Start.Add(SampleAggregateTestData.Hour).ToOffset(TimeSpan.FromHours(2));
        SampleAggregateTestData.Append(db,
            SampleAggregateTestData.Data(1, SampleAggregateTestData.Start, 1),
            SampleAggregateTestData.Data(2, cutWithOffset, 2),
            SampleAggregateTestData.Data(3, cutWithOffset.ToUniversalTime(), 3),
            SampleAggregateTestData.Data(4, cutWithOffset.AddTicks(1), 4));

        var latest = db.Database.ReadLatestSample(SampleAggregateTestData.RootPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series, cutWithOffset));
        var later = db.Database.ReadLatestSample(SampleAggregateTestData.RootPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series));

        await Assert.That(latest.Sample?.Sample.Value).IsEqualTo(3d);
        await Assert.That(latest.Sample?.Sequence).IsEqualTo(3L);
        await Assert.That(later.Sample?.Sample.Value).IsEqualTo(4d);
        await Assert.That(later.Sample?.Sequence).IsEqualTo(4L);
    }

    [Test]
    public async Task AcSeries008LatestIncludesDateMaximumAndReturnsNullWhenNoSampleIsAtCut()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        SampleAggregateTestData.Append(db,
            SampleAggregateTestData.Data(1, DateTimeOffset.MinValue, 1),
            SampleAggregateTestData.Data(2, DateTimeOffset.MaxValue, 2));

        var maximum = db.Database.ReadLatestSample(SampleAggregateTestData.RootPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series));
        var minimum = db.Database.ReadLatestSample(SampleAggregateTestData.RootPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series, DateTimeOffset.MinValue));
        var absent = db.Database.ReadLatestSample(SampleAggregateTestData.RootPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.EmptySeries,
                DateTimeOffset.MinValue));

        await Assert.That(maximum.Sample?.Sample.Timestamp).IsEqualTo(DateTimeOffset.MaxValue);
        await Assert.That(minimum.Sample?.Sample.Timestamp).IsEqualTo(DateTimeOffset.MinValue);
        await Assert.That(absent.Sample).IsNull();
    }

    [Test]
    public async Task AcSeries008LatestVisitsOneBaselineEntryWithLargeOlderHistoryAndSmallRawBudget()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        var samples = Enumerable.Range(1, SampleAggregateTestData.SmallHistoryCount)
            .Select(index => SampleAggregateTestData.Data(index,
                SampleAggregateTestData.Start.AddTicks(index), index)).ToArray();
        SampleAggregateTestData.Append(db, samples);
        var diagnostics = db.Store.GetReadDiagnostics();

        var bounded = new DatabaseEngine(db.Store, db.Database.Authorization,
            new() { MaxQueryReadBytes = SampleAggregateTestData.RawReadBudgetBytes });
        var result = bounded.ReadLatestSample(SampleAggregateTestData.RootPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series));
        var after = db.Store.GetReadDiagnostics();

        await Assert.That(result.Sample?.Sample.Value).IsEqualTo((double)SampleAggregateTestData.SmallHistoryCount);
        await Assert.That(after.RangeBaselineEntries - diagnostics.RangeBaselineEntries).IsEqualTo(1L);
    }
}
