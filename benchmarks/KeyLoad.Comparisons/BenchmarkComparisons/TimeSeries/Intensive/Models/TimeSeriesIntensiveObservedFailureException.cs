namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveObservedFailureException : InvalidOperationException
{
    public TimeSeriesIntensiveObservedFailureException() : base(nameof(TimeSeriesIntensiveObservedFailureException))
    {
    }

    public TimeSeriesIntensiveObservedFailureException(string message) : base(message)
    {
    }

    public TimeSeriesIntensiveObservedFailureException(string message, Exception innerException) : base(message, innerException)
    {
    }

    private TimeSeriesIntensiveObservedFailureException(TimeSeriesIntensiveOutcome completion, Exception original)
        : base(nameof(TimeSeriesIntensiveObservedFailureException), original)
    {
        Completion = completion;
    }

    internal TimeSeriesIntensiveOutcome Completion { get; } = TimeSeriesIntensiveOutcome.UnexpectedFailure;

    internal static Exception Observe(TimeSeriesIntensiveOutcome completion, Exception original)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (completion is not (TimeSeriesIntensiveOutcome.Succeeded or TimeSeriesIntensiveOutcome.DeadlineExceeded or TimeSeriesIntensiveOutcome.Cancelled))
        {
            throw new ArgumentOutOfRangeException(nameof(completion));
        }

        return completion == TimeSeriesIntensiveOutcome.Succeeded || !TimeSeriesIntensiveExceptionBoundary.IsNonfatal(original)
            ? original : new TimeSeriesIntensiveObservedFailureException(completion, original);
    }
}
