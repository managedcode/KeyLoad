using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveSummaryTests
{
    [Test]
    public async Task AcTsi004NearestRankUsesAllAttemptsIncludingFailedLatencies()
    {
        var attempts = Enumerable.Range(0, 10000).Select(index => TimeSeriesIntensiveAttemptValues.Success(0, index)).ToArray();
        attempts[17] = attempts[17] with
        {
            Outcome = TimeSeriesIntensiveOutcome.TargetFailure,
            Failure = new(TimeSeriesIntensiveFailureOrigin.KeyLoad, ErrorCode.ResourceExhausted, 429, null)
        };
        var actual = TimeSeriesIntensiveSummary.Calculate(attempts, 40000, 1000);
        await Assert.That(actual.Attempted).IsEqualTo(10000);
        await Assert.That(actual.Succeeded).IsEqualTo(9999);
        await Assert.That(actual.WallSeconds).IsEqualTo(40d);
        await Assert.That(actual.Throughput).IsEqualTo(9999d / 40);
        await Assert.That(actual.ValidationWorkerSeconds).IsEqualTo(20d);
        await Assert.That(actual.P50Milliseconds).IsEqualTo(5000d);
        await Assert.That(actual.P95Milliseconds).IsEqualTo(9500d);
        await Assert.That(actual.P99Milliseconds).IsEqualTo(9900d);
        await Assert.That(actual.Publishable).IsFalse();
    }

    [Test]
    public async Task AcTsi004FiveSummaryMedianAndNearestRankHaveIndependentGoldens()
    {
        await Assert.That(TimeSeriesIntensiveSummary.MedianFive([99, 1, 13, 7, 5])).IsEqualTo(7d);
        await Assert.That(TimeSeriesIntensiveSummary.Percentile([100, 2, 9, 3], 0.5)).IsEqualTo(3L);
        await Assert.That(TimeSeriesIntensiveSummary.Percentile([100, 2, 9, 3], 0.95)).IsEqualTo(100L);
        Assert.ThrowsExactly<ArgumentException>(() => TimeSeriesIntensiveSummary.MedianFive([1, 2, 3]));
        Assert.ThrowsExactly<ArgumentException>(() => TimeSeriesIntensiveSummary.MedianFive([1, 2, double.NaN, 4, 5]));
        Assert.ThrowsExactly<ArgumentException>(() => TimeSeriesIntensiveSummary.Percentile([], 0.5));
    }

    [Test]
    public void AcTsi004MissingSlotsAndInvalidDurationsCannotBecomeMeasurements()
    {
        var attempts = Enumerable.Range(0, 10000).Select(index => TimeSeriesIntensiveAttemptValues.Success(0, index)).ToArray();
        attempts[345] = default;
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveSummary.Calculate(attempts, 1, 1));
        attempts[345] = TimeSeriesIntensiveAttemptValues.Success(0, 345) with { LatencyTicks = -1 };
        Assert.ThrowsExactly<ComparisonFailureException>(() => TimeSeriesIntensiveSummary.Calculate(attempts, 1, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TimeSeriesIntensiveSummary.Calculate(attempts, 0, 1));
    }
}
