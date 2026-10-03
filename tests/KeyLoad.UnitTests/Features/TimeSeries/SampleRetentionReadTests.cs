using KeyLoad.Core;
using KeyLoad.Core.Features.TimeSeries;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleRetentionReadTests
{
    [Test]
    public async Task AcSeries014EveryReaderAppliesFloorBeforePhysicalPurgeCompletesAndWindowsKeepOriginalAnchor()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        var start = SampleAggregateTestData.Start;
        var floor = start.AddSeconds(2);
        var end = start.AddSeconds(3);
        SampleAggregateTestData.Append(db,
            SampleAggregateTestData.Data(1, start, 1),
            SampleAggregateTestData.Data(2, start.AddSeconds(1), 2),
            SampleAggregateTestData.Data(3, floor, 3));
        db.Commit(new ExpireSamples(SampleAggregateTestData.Set, SampleAggregateTestData.Series, floor, 1));

        var rawBeforePurge = db.Store.Read(view => view.VisitRange(
            SampleReadKeys.Prefix(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series),
            10, static (_, _) => true, untilKey: SampleReadKeys.FromInclusive(db.Partition,
                SampleAggregateTestData.Set, SampleAggregateTestData.Series, floor)));
        var ranged = db.Database.ReadSamples(SampleAggregateTestData.RootPrincipal, db.Partition,
            SampleAggregateTestData.Set, SampleAggregateTestData.Series, start, floor);
        var oldLatest = db.Database.ReadLatestSample(SampleAggregateTestData.RootPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series, start.AddSeconds(1)));
        var latestAtFloor = db.Database.ReadLatestSample(SampleAggregateTestData.RootPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series, floor));
        var rawAggregate = db.Database.AggregateSamples(SampleAggregateTestData.RootPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series, start, end));
        var windows = db.Database.AggregateSampleWindows(SampleAggregateTestData.RootPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series, start, end,
                TimeSpan.FromSeconds(1)));

        await Assert.That(rawBeforePurge.Records).IsEqualTo(1);
        await Assert.That(rawBeforePurge.HasMore).IsFalse();
        await Assert.That(ranged.Select(record => record.Sample.EventId)).IsEquivalentTo(new[] { "sample-3" });
        await Assert.That(oldLatest.Sample).IsNull();
        await Assert.That(latestAtFloor.Sample?.Sample.EventId).IsEqualTo("sample-3");
        await Assert.That(rawAggregate.Count).IsEqualTo(1L);
        await Assert.That(rawAggregate.Sum).IsEqualTo(3d);
        await Assert.That(windows.Windows.Select(window => window.Aggregate.Count)).IsEquivalentTo(new long[] { 0, 0, 1 });
        await Assert.That(windows.Windows.Select(window => window.From)).IsEquivalentTo(new[]
        {
            start, start.AddSeconds(1), start.AddSeconds(2)
        });
    }

    [Test]
    public async Task AcSeries014FloorAfterRequestedWindowRangeKeepsDenseEmptyAnchors()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        var start = SampleAggregateTestData.Start;
        var floor = start.AddSeconds(3);
        db.Commit(new ExpireSamples(SampleAggregateTestData.Set, SampleAggregateTestData.Series, floor));

        var windows = db.Database.AggregateSampleWindows(SampleAggregateTestData.RootPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series, start,
                start.AddSeconds(2), TimeSpan.FromSeconds(1)));

        await Assert.That(windows.Windows.Select(window => window.Aggregate.Count)).IsEquivalentTo(new long[] { 0, 0 });
        await Assert.That(windows.Windows.Select(window => window.From)).IsEquivalentTo(new[] { start, start.AddSeconds(1) });
    }

    [Test]
    public async Task AcSeries014MaximumTimestampRemainsVisibleAndOffsetCutoffIsComparedAsUtc()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        SampleAggregateTestData.Append(db,
            SampleAggregateTestData.Data(1, DateTimeOffset.MinValue, 1),
            SampleAggregateTestData.Data(2, DateTimeOffset.MaxValue, 2));
        var offsetFloor = DateTimeOffset.MaxValue.ToOffset(TimeSpan.Zero);
        db.Commit(new ExpireSamples(SampleAggregateTestData.Set, SampleAggregateTestData.Series,
            offsetFloor, 1));

        var range = db.Database.ReadSamples(SampleAggregateTestData.RootPrincipal, db.Partition,
            SampleAggregateTestData.Set, SampleAggregateTestData.Series, DateTimeOffset.MinValue,
            DateTimeOffset.MaxValue);
        var latest = db.Database.ReadLatestSample(SampleAggregateTestData.RootPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series));
        var aggregate = db.Database.AggregateSamples(SampleAggregateTestData.RootPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series,
                DateTimeOffset.MinValue));
        var status = db.Database.ReadSampleRetention(SampleAggregateTestData.RootPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series));

        await Assert.That(range.Select(row => row.Sample.EventId)).IsEquivalentTo(new[] { "sample-2" });
        await Assert.That(latest.Sample?.Sample.Timestamp).IsEqualTo(DateTimeOffset.MaxValue);
        await Assert.That(aggregate.Count).IsEqualTo(1L);
        await Assert.That(aggregate.Sum).IsEqualTo(2d);
        await Assert.That(status.Before).IsEqualTo(DateTimeOffset.MaxValue);
        await Assert.That(status.Before?.Offset).IsEqualTo(TimeSpan.Zero);
    }
}
