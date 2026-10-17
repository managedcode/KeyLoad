using System.Collections.Immutable;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveValidationTests
{
    [Test]
    public async Task AcTsi002CorruptEventTimestampValueAndTagsCannotPassOrderedEquality()
    {
        var expected = TimeSeriesIntensiveOracle.Raw(TimeSeriesIntensivePlans.Read(9));
        var first = expected[0];
        var corruptions = new[]
        {
            first with { Sample = first.Sample with { EventId = "foreign" } },
            first with { Sample = first.Sample with { Timestamp = first.Sample.Timestamp.AddTicks(1) } },
            first with { Sample = first.Sample with { Value = first.Sample.Value + 0.25 } },
            first with { Sample = first.Sample with { Value = double.NaN } },
            first with { Sample = null! },
            first with { TagsJson = "{" },
            first with { TagsJson = null! }
        };
        foreach (var row in corruptions)
        {
            Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveOracle.ValidateRaw(expected, expected.SetItem(0, row)));
        }

        var latest = TimeSeriesIntensiveOracle.Latest(TimeSeriesIntensivePlans.Read(9))!;
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveOracle.ValidateLatest(latest, null));
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveOracle.ValidateLatest(latest, latest with { Sequence = latest.Sequence - 1 }));
        await Assert.That(corruptions.Length).IsEqualTo(7);
    }

    [Test]
    public async Task AcTsi005WindowOrderBoundsCardinalityAndStatisticsAreAllChecked()
    {
        var expected = TimeSeriesIntensiveOracle.Windows(TimeSeriesIntensivePlans.Read(3));
        TimeSeriesIntensiveOracle.ValidateWindows(expected, expected);
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveOracle.ValidateWindows(expected, default));
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveOracle.ValidateWindows(expected, expected.RemoveAt(0)));
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveOracle.ValidateWindows(expected, expected.Add(expected[0])));
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveOracle.ValidateWindows(expected, expected.Reverse().ToImmutableArray()));
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveOracle.ValidateWindows(expected, expected.SetItem(0, null!)));
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveOracle.ValidateWindows(expected, expected.SetItem(0, expected[0] with { UntilExclusive = null })));
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveOracle.ValidateWindows(expected, expected.SetItem(0, expected[0] with { Aggregate = null! })));
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveOracle.ValidateWindows(expected, expected.SetItem(0, expected[0] with { Aggregate = expected[0].Aggregate with { Count = 0 } })));
        await Assert.That(expected.Length).IsEqualTo(54);
    }

    [Test]
    public async Task AcTsi005IntegerQuarterFoldMatchesIndependentRangeEnumeration()
    {
        for (var group = 0; group < 224; group++)
        {
            var quarters = new List<int>();
            for (var original = 0; original < 4096; original++)
            {
                var minute = original / 16 * 5 + original % 16 / 4;
                if (minute >= group * 5 + 1 && minute < group * 5 + 161)
                {
                    quarters.Add(((original / 16 + 1729) % 31 - 15) * 4 + original % 16 - 8);
                }
            }

            var expected = new SampleAggregate(quarters.Count, quarters.Sum() / 4d,
                quarters.Min() / 4d, quarters.Max() / 4d, quarters.Sum() / (4d * quarters.Count));
            await Assert.That(TimeSeriesIntensiveOracle.Aggregate(TimeSeriesIntensivePlans.Read(group))).IsEqualTo(expected);
        }
    }

    [Test]
    public async Task AcTsi005EmptyRawEqualAggregateAndInvalidOraclePlansAreExplicit()
    {
        var epoch = TimeSeriesIntensiveProfile.Epoch;
        var before = new TimeSeriesIntensiveReadPlan(epoch.AddMinutes(-2), epoch.AddMinutes(-1), epoch.AddTicks(-1));
        await Assert.That(TimeSeriesIntensiveOracle.Raw(before).Length).IsEqualTo(0);
        await Assert.That(TimeSeriesIntensiveOracle.Aggregate(epoch, epoch)).IsEqualTo(new SampleAggregate(0, 0, null, null, null));
        await Assert.That(TimeSeriesIntensiveOracle.Windows(epoch, epoch, TimeSpan.FromMinutes(1)).Length).IsEqualTo(0);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TimeSeriesIntensiveOracle.Raw(before with { Until = before.From.AddTicks(-1) }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TimeSeriesIntensiveOracle.Aggregate(epoch, epoch.AddTicks(-1)));
        Assert.ThrowsExactly<ArgumentException>(() => TimeSeriesIntensiveOracle.SeedReadback(TimeSeriesIntensivePlans.SeedReadbacks().First() with { ExpectedCount = 257 }));
    }
}
