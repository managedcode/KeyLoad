using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveAttemptValues
{
    internal static TimeSeriesIntensiveAttempt Success(int repetition, int index) => new(repetition, index,
        index % 16, index + 1L, 2, TimeSeriesIntensiveOutcome.Succeeded, 516,
        TimeSeriesIntensiveHash.Parse("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f"), 0, default);
}
