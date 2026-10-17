namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveCallDeadlineException : TimeoutException
{
    public TimeSeriesIntensiveCallDeadlineException() : base(TimeSeriesIntensiveRuntimeErrors.CallDeadline)
    {
    }

    public TimeSeriesIntensiveCallDeadlineException(string message) : base(message)
    {
    }

    public TimeSeriesIntensiveCallDeadlineException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
