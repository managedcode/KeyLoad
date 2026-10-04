using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleAggregateWindowTests
{
    [Test]
    public async Task AcSeries010WindowsAreDenseAnchoredClampedAndMatchRawReference()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        SampleAggregateTestData.Append(db,
            SampleAggregateTestData.Data(1, SampleAggregateTestData.Start, -4),
            SampleAggregateTestData.Data(2, SampleAggregateTestData.Start.AddTicks(1), 10),
            SampleAggregateTestData.Data(3, SampleAggregateTestData.Start.Add(SampleAggregateTestData.Minute), 2),
            SampleAggregateTestData.Data(4, SampleAggregateTestData.Start.Add(SampleAggregateTestData.TwoMinutes), 6));
        var end = SampleAggregateTestData.Start.Add(SampleAggregateTestData.TwoMinutes).AddTicks(1);
        var raw = db.Database.ReadSamples(SampleAggregateTestData.RootPrincipal, db.Partition,
            SampleAggregateTestData.Set, SampleAggregateTestData.Series, SampleAggregateTestData.Start, end);

        var actual = db.Database.AggregateSampleWindows(SampleAggregateTestData.RootPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series,
                SampleAggregateTestData.Start, end, SampleAggregateTestData.Minute));

        await Assert.That(actual.Windows.Length).IsEqualTo(SampleAggregateTestData.ExactWindowCount);
        await Assert.That(actual.Windows[0].From).IsEqualTo(SampleAggregateTestData.Start);
        await Assert.That(actual.Windows[1].From).IsEqualTo(
            SampleAggregateTestData.Start.Add(SampleAggregateTestData.Minute));
        await Assert.That(actual.Windows[2].From).IsEqualTo(
            SampleAggregateTestData.Start.Add(SampleAggregateTestData.TwoMinutes));
        await Assert.That(actual.Windows[0].Aggregate).IsEqualTo(SampleAggregateTestData.Oracle(
            raw.Where(sample => sample.Sample.Timestamp < SampleAggregateTestData.Start.Add(SampleAggregateTestData.Minute))));
        await Assert.That(actual.Windows[1].Aggregate).IsEqualTo(SampleAggregateTestData.Oracle(
            raw.Where(sample => sample.Sample.Timestamp >= SampleAggregateTestData.Start.Add(SampleAggregateTestData.Minute)
                && sample.Sample.Timestamp < SampleAggregateTestData.Start.Add(SampleAggregateTestData.TwoMinutes))));
        await Assert.That(actual.Windows[2].Aggregate).IsEqualTo(SampleAggregateTestData.Oracle(
            raw.Where(sample => sample.Sample.Timestamp >= SampleAggregateTestData.Start.Add(SampleAggregateTestData.TwoMinutes))));
        await Assert.That(actual.Windows[^1].UntilExclusive).IsEqualTo(end);
    }

    [Test]
    public async Task AcSeries010MaximumTickUsesNullEndAndWidthMayExceedRange()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        SampleAggregateTestData.Append(db, SampleAggregateTestData.Data(1, DateTimeOffset.MaxValue, 5));

        var latestTick = db.Database.AggregateSampleWindows(SampleAggregateTestData.RootPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series,
                DateTimeOffset.MaxValue, null, SampleAggregateTestData.Hour));
        var oneTick = db.Database.AggregateSampleWindows(SampleAggregateTestData.RootPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series,
                DateTimeOffset.MaxValue, DateTimeOffset.MaxValue, SampleAggregateTestData.Hour));

        await Assert.That(latestTick.Windows.Length).IsEqualTo(SampleAggregateTestData.OneResult);
        await Assert.That(latestTick.Windows[0].UntilExclusive).IsNull();
        await Assert.That(latestTick.Windows[0].Aggregate.Count).IsEqualTo(1L);
        await Assert.That(oneTick.Windows.Length).IsEqualTo(SampleAggregateTestData.NoResults);
    }

    [Test]
    public async Task AcSeries010EmptyRangesRemainDenseAndInvalidWidthOrInvertedRangesFailValidation()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        var request = new AggregateSampleWindowsRequest(db.Partition, SampleAggregateTestData.Set,
            SampleAggregateTestData.EmptySeries, SampleAggregateTestData.Start,
            SampleAggregateTestData.Start.Add(SampleAggregateTestData.ThreeMinutes), SampleAggregateTestData.Minute);

        var empty = db.Database.AggregateSampleWindows(SampleAggregateTestData.RootPrincipal, request);
        var invalidWidth = SampleAggregateTestData.Failure(() => db.Database.AggregateSampleWindows(
            SampleAggregateTestData.RootPrincipal, request with { Width = TimeSpan.Zero }), ErrorCode.Validation);
        var inverted = SampleAggregateTestData.Failure(() => db.Database.AggregateSampleWindows(
            SampleAggregateTestData.RootPrincipal, request with { From = request.UntilExclusive!.Value.AddTicks(1) }),
            ErrorCode.Validation);

        await Assert.That(empty.Windows.Length).IsEqualTo(SampleAggregateTestData.ExactWindowCount);
        await Assert.That(empty.Windows.All(window => window.Aggregate.Count == 0
            && window.Aggregate.Minimum is null && window.Aggregate.Maximum is null && window.Aggregate.Average is null)).IsTrue();
        await Assert.That(invalidWidth.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(inverted.Code).IsEqualTo(ErrorCode.Validation);
    }

    [Test]
    public async Task AcSeries010CalculatedWindowCountIsRejectedBeforeResultAllocation()
    {
        using var db = new TestDatabase(new DatabaseLimits { MaxResults = SampleAggregateTestData.ServerWindowLimit });
        SampleAggregateTestData.Configure(db);
        var request = new AggregateSampleWindowsRequest(db.Partition, SampleAggregateTestData.Set,
            SampleAggregateTestData.EmptySeries, SampleAggregateTestData.Start,
            SampleAggregateTestData.Start.Add(SampleAggregateTestData.ThreeMinutes), SampleAggregateTestData.Minute,
            MaxWindows: SampleAggregateTestData.ServerWindowLimit);

        var exact = db.Database.AggregateSampleWindows(SampleAggregateTestData.RootPrincipal, request);
        var oneOverCaller = SampleAggregateTestData.Failure(() => db.Database.AggregateSampleWindows(
            SampleAggregateTestData.RootPrincipal, request with { MaxWindows = SampleAggregateTestData.ServerWindowLimit - 1 }),
            ErrorCode.BudgetExceeded);
        var overServer = SampleAggregateTestData.Failure(() => db.Database.AggregateSampleWindows(
            SampleAggregateTestData.RootPrincipal, request with { MaxWindows = SampleAggregateTestData.ServerWindowLimit + 1 }),
            ErrorCode.BudgetExceeded);

        await Assert.That(exact.Windows.Length).IsEqualTo(SampleAggregateTestData.ExactWindowCount);
        await Assert.That(oneOverCaller.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(overServer.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }
}
