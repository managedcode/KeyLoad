using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensivePreparedExpectationTests
{
    [Test]
    public async Task AcTsi002PreparedRangesAndAllStatisticsMatchIndependentReference()
    {
        var actual = TimeSeriesIntensiveExpectations.Create();
        await Assert.That(actual.Raw.Length).IsEqualTo(224);
        await Assert.That(actual.Aggregates.Length).IsEqualTo(224);
        await Assert.That(actual.Windows.Length).IsEqualTo(224);
        for (var group = 0; group < 224; group++)
        {
            var expected = TimeSeriesIntensiveReferenceOracle.Windows(group);
            await Assert.That(actual.Raw[group].SequenceEqual(TimeSeriesIntensiveReferenceOracle.Raw(group))).IsTrue();
            await Assert.That(actual.Windows[group].SequenceEqual(expected)).IsTrue();
            var aggregate = actual.Aggregates[group];
            var count = expected.Sum(window => window.Aggregate.Count);
            var sum = expected.Sum(window => window.Aggregate.Sum);
            await Assert.That(aggregate).IsEqualTo(new SampleAggregate(count, sum,
                expected.Min(window => window.Aggregate.Minimum), expected.Max(window => window.Aggregate.Maximum), sum / count));
            await Assert.That(actual.Ranges[group].From).IsEqualTo(TimeSeriesIntensiveReferenceOracle.Epoch.AddMinutes(group * 5 + 1));
            await Assert.That(actual.Ranges[group].Until).IsEqualTo(TimeSeriesIntensiveReferenceOracle.Epoch.AddMinutes(group * 5 + 161));
        }
    }

    [Test]
    public async Task AcTsi005PreparedLatestAndAppendInputsMatchIndependentReference()
    {
        var actual = TimeSeriesIntensiveExpectations.Create();
        await Assert.That(actual.Latest.Length).IsEqualTo(256);
        for (var group = 0; group < 256; group++)
        {
            var cut = TimeSeriesIntensiveReferenceOracle.Epoch.AddMinutes(group * 5 + 3);
            await Assert.That(actual.LatestCuts[group]).IsEqualTo(cut);
            await Assert.That(actual.Latest[group]).IsEqualTo(TimeSeriesIntensiveReferenceOracle.Latest(cut));
        }

        await Assert.That(actual.AppendSamples.Length).IsEqualTo(10000);
        for (var index = 0; index < 10000; index++)
        {
            await Assert.That(actual.AppendSamples[index]).IsEqualTo(TimeSeriesIntensiveReferenceOracle.Append(index, "unused", 1).Sample);
        }
    }
}
