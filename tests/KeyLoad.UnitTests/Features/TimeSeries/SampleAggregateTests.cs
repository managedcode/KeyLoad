using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleAggregateTests
{
    [Test]
    public async Task AcSeries009AggregateMatchesIndependentRawFoldForLateDuplicatesOffsetsAndEdges()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        var offset = SampleAggregateTestData.Start.Add(SampleAggregateTestData.Minute).ToOffset(TimeSpan.FromHours(-5));
        SampleAggregateTestData.Append(db,
            SampleAggregateTestData.Data(1, SampleAggregateTestData.Start, -4),
            SampleAggregateTestData.Data(2, offset, 0),
            SampleAggregateTestData.Data(3, offset.ToUniversalTime(), 6),
            SampleAggregateTestData.Data(4, SampleAggregateTestData.Start.Add(SampleAggregateTestData.TwoMinutes), 10));
        SampleAggregateTestData.Append(db,
            SampleAggregateTestData.Data(2, offset, 0),
            SampleAggregateTestData.Data(5, SampleAggregateTestData.Start.AddSeconds(30), -2));
        SampleAggregateTestData.Append(db, SampleAggregateTestData.Data(5, SampleAggregateTestData.Start, 2));
        var raw = db.Database.ReadSamples(SampleAggregateTestData.RootPrincipal, db.Partition,
            SampleAggregateTestData.Set, SampleAggregateTestData.Series, SampleAggregateTestData.Start,
            SampleAggregateTestData.Start.Add(SampleAggregateTestData.TwoMinutes).AddTicks(-1));
        var expected = SampleAggregateTestData.Oracle(raw);

        var actual = db.Database.AggregateSamples(SampleAggregateTestData.RootPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series,
                SampleAggregateTestData.Start, SampleAggregateTestData.Start.Add(SampleAggregateTestData.TwoMinutes)));

        await Assert.That(actual.Count).IsEqualTo(expected.Count);
        await Assert.That(actual.Sum).IsEqualTo(expected.Sum);
        await Assert.That(actual.Minimum).IsEqualTo(expected.Minimum);
        await Assert.That(actual.Maximum).IsEqualTo(expected.Maximum);
        await Assert.That(actual.Average).IsEqualTo(expected.Average);
    }

    [Test]
    public async Task AcSeries009NullEndIncludesMaximumAndEqualBoundsReturnEmptyAggregate()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        SampleAggregateTestData.Append(db,
            SampleAggregateTestData.Data(1, DateTimeOffset.MinValue, -1),
            SampleAggregateTestData.Data(2, DateTimeOffset.MaxValue, 9));

        var all = db.Database.AggregateSamples(SampleAggregateTestData.RootPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series, DateTimeOffset.MinValue));
        var empty = db.Database.AggregateSamples(SampleAggregateTestData.RootPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series,
                DateTimeOffset.MaxValue, DateTimeOffset.MaxValue));

        await Assert.That(all.Count).IsEqualTo(2L);
        await Assert.That(all.Sum).IsEqualTo(8d);
        await Assert.That(empty.Count).IsEqualTo(0L);
        await Assert.That(empty.Sum).IsEqualTo(0d);
        await Assert.That(empty.Minimum).IsNull();
        await Assert.That(empty.Maximum).IsNull();
        await Assert.That(empty.Average).IsNull();
    }

    [Test]
    public async Task AcSeries009FiniteSumOverflowFailsWithoutChangingPositionOrHealthyFollowup()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        SampleAggregateTestData.Append(db,
            SampleAggregateTestData.Data(1, SampleAggregateTestData.Start, double.MaxValue),
            SampleAggregateTestData.Data(2, SampleAggregateTestData.Start.AddTicks(1), double.MaxValue));
        var position = db.Store.Position;

        var failure = SampleAggregateTestData.Failure(() => db.Database.AggregateSamples(
            SampleAggregateTestData.RootPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series,
                SampleAggregateTestData.Start)), ErrorCode.Validation);
        var healthy = db.Database.AggregateSamples(SampleAggregateTestData.RootPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series,
                SampleAggregateTestData.Start, SampleAggregateTestData.Start.AddTicks(1)));

        await Assert.That(failure.Message).DoesNotContain(nameof(double.MaxValue));
        await Assert.That(db.Store.Position).IsEqualTo(position);
        await Assert.That(healthy.Count).IsEqualTo(1L);
        await Assert.That(healthy.Sum).IsEqualTo(double.MaxValue);
    }
}
