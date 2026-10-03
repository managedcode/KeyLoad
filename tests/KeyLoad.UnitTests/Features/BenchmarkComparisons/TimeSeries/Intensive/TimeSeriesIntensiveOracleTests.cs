using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveOracleTests
{
    [Test]
    public async Task AcTsi005InclusiveRawHalfOpenStatisticsAndClampedWindowsMatchGoldens()
    {
        var plan = TimeSeriesIntensivePlans.Read(0);
        var raw = TimeSeriesIntensiveOracle.Raw(plan);
        var aggregate = TimeSeriesIntensiveOracle.Aggregate(plan);
        var windows = TimeSeriesIntensiveOracle.Windows(plan);
        await Assert.That(raw.Length).IsEqualTo(516);
        await Assert.That(raw[0].Sample.EventId).IsEqualTo("s-000007");
        await Assert.That(raw[3].Sample.EventId).IsEqualTo("s-000004");
        await Assert.That(raw[^1].Sample.EventId).IsEqualTo("s-000516");
        await Assert.That(aggregate).IsEqualTo(new SampleAggregate(512, 84, -17, 16.75, 0.1640625));
        await Assert.That(windows.Length).IsEqualTo(54);
        await Assert.That(windows.Sum(row => row.Aggregate.Count)).IsEqualTo(512L);
        for (var index = 0; index < 53; index++)
        {
            var count = new long[] { 12, 8, 8, 12, 8 }[index % 5];
            await Assert.That(windows[index].Aggregate.Count).IsEqualTo(count);
            await Assert.That(windows[index].UntilExclusive - windows[index].From).IsEqualTo(TimeSpan.FromMinutes(3));
        }

        await Assert.That(windows[^1].UntilExclusive).IsEqualTo(plan.Until);
        await Assert.That(windows[^1].Aggregate.Count).IsEqualTo(4L);
        await Assert.That(windows[^1].UntilExclusive - windows[^1].From).IsEqualTo(TimeSpan.FromMinutes(1));
    }

    [Test]
    public async Task AcTsi005TrueEmptyGapAndAbsentLatestRetainNullStatistics()
    {
        var from = TimeSeriesIntensiveProfile.Epoch.AddMinutes(4);
        var gap = TimeSeriesIntensiveOracle.Windows(from, from.AddMinutes(1), TimeSpan.FromMinutes(1));
        await Assert.That(gap.Length).IsEqualTo(1);
        await Assert.That(gap[0].Aggregate).IsEqualTo(new SampleAggregate(0, 0, null, null, null));
        await Assert.That(TimeSeriesIntensiveOracle.Latest(TimeSeriesIntensiveProfile.Epoch.AddTicks(-1))).IsNull();
        TimeSeriesIntensiveOracle.ValidateLatest(null, null);
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveOracle.ValidateLatest(null, TimeSeriesIntensiveCorpus.SeedInsertion[0]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TimeSeriesIntensiveOracle.Windows(from, from.AddMinutes(-1), TimeSpan.FromMinutes(1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TimeSeriesIntensiveOracle.Windows(from, from, TimeSpan.Zero));
    }

    [Test]
    public async Task AcTsi002ActualRawOrderIdentityAndCanonicalTagsAreCheckedDirectly()
    {
        var expected = TimeSeriesIntensiveOracle.Raw(TimeSeriesIntensivePlans.Read(0));
        var equivalent = expected.SetItem(0, expected[0] with
        {
            Sample = expected[0].Sample with { Timestamp = expected[0].Sample.Timestamp.ToOffset(TimeSpan.FromHours(2)) },
            TagsJson = "{ \"revision\": 1, \"kind\": \"intensive\" }"
        });
        TimeSeriesIntensiveOracle.ValidateRaw(expected, equivalent);
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveOracle.ValidateRaw(expected, expected.RemoveAt(0)));
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveOracle.ValidateRaw(expected, expected.Add(expected[0])));
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveOracle.ValidateRaw(expected, expected.SetItem(0, expected[1]).SetItem(1, expected[0])));
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveOracle.ValidateRaw(expected, expected.SetItem(0, expected[0] with { Sequence = -1 })));
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveOracle.ValidateRaw(expected, expected.SetItem(0, expected[0] with { SeriesId = "other" })));
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveOracle.ValidateRaw(expected, expected.SetItem(0, expected[0] with { TagsJson = "{}" })));
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveOracle.ValidateRaw(expected, default));
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveOracle.ValidateRaw(expected, expected.SetItem(0, null!)));
        await Assert.That(equivalent.Length).IsEqualTo(516);
    }

    [Test]
    public async Task AcTsi005FiniteStatisticsPrecedeAbsoluteAverageTolerance()
    {
        var expected = new SampleAggregate(3, 1, -1, 1, 1d / 3);
        TimeSeriesIntensiveOracle.ValidateAggregate(expected, expected with { Average = expected.Average + 5e-13 });
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveOracle.ValidateAggregate(expected, expected with { Average = invalid }));
            Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveOracle.ValidateAggregate(expected, expected with { Sum = invalid }));
            Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveOracle.ValidateAggregate(expected, expected with { Minimum = invalid }));
            Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveOracle.ValidateAggregate(expected, expected with { Maximum = invalid }));
        }

        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveOracle.ValidateAggregate(expected, expected with { Average = expected.Average + 2e-12 }));
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveOracle.ValidateAggregate(expected, expected with { Maximum = null }));
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveOracle.ValidateAggregate(new(0, 0, null, null, null), new(0, 0, 0, null, null)));
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveOracle.ValidateFullCount("measured-r0", new(9999, 0, 0, 0, 0)));
        await Assert.That(expected.Count).IsEqualTo(3L);
    }
}
