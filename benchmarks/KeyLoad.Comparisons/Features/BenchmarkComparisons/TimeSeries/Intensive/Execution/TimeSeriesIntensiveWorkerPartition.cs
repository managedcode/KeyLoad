namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveWorkerPartition
{
    internal static IEnumerable<int> Indices(int worker, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(worker);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(worker, TimeSeriesIntensiveProfile.Concurrency);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, TimeSeriesIntensiveProfile.OperationCount);
        return Enumerate(worker, count);
    }

    private static IEnumerable<int> Enumerate(int worker, int count)
    {
        for (var index = worker; index < count; index += TimeSeriesIntensiveProfile.Concurrency)
        {
            yield return index;
        }
    }
}
