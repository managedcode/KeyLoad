namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveTargetCompletionException : InvalidOperationException
{
    public TimeSeriesIntensiveTargetCompletionException() : base(nameof(TimeSeriesIntensiveTargetCompletionException))
    {
    }

    public TimeSeriesIntensiveTargetCompletionException(string message) : base(message)
    {
    }

    public TimeSeriesIntensiveTargetCompletionException(string message, Exception innerException) : base(message, innerException)
    {
    }

    private TimeSeriesIntensiveTargetCompletionException(Exception selected, TimeSeriesIntensiveFailure cleanup,
        TimeSeriesIntensiveAcknowledgement? acknowledgement)
        : base(nameof(TimeSeriesIntensiveTargetCompletionException), selected)
    {
        CleanupFailure = cleanup;
        Acknowledgement = acknowledgement;
    }

    internal TimeSeriesIntensiveFailure CleanupFailure { get; }
    internal TimeSeriesIntensiveAcknowledgement? Acknowledgement { get; }

    internal static Exception? Join(Exception? primary, Exception? cleanup, TimeSeriesIntensiveAcknowledgement? acknowledgement)
    {
        if (acknowledgement is { } actual)
        {
            TimeSeriesIntensiveAcknowledgement.Validate(actual);
        }
        if (primary is not null && !TimeSeriesIntensiveExceptionBoundary.IsNonfatal(primary))
        {
            return primary;
        }
        if (cleanup is not null && !TimeSeriesIntensiveExceptionBoundary.IsNonfatal(cleanup))
        {
            return cleanup;
        }
        var selected = primary ?? cleanup;
        if (selected is null || cleanup is null && acknowledgement is null)
        {
            return selected;
        }
        var cleanupFact = cleanup is null ? default : TimeSeriesIntensiveFailure.Capture(cleanup).Failure;
        return new TimeSeriesIntensiveTargetCompletionException(selected, cleanupFact, acknowledgement);
    }
}
