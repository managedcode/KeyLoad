namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveAttemptFailures
{
    internal static TimeSeriesIntensiveAttempt Capture(int repetition, int index, int worker, long latency,
        TimeSeriesIntensiveOutcome completion, Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var captured = TimeSeriesIntensiveFailure.Capture(error);
        var outcome = completion == TimeSeriesIntensiveOutcome.Succeeded ? captured.Outcome : completion;
        var facts = Facts(error);
        return new(repetition, index, worker, latency, default, outcome, null, default,
            facts.Acknowledgement?.Sequence ?? facts.ObservedSequence, captured.Failure)
        {
            Acknowledgement = facts.Acknowledgement,
            CleanupFailure = facts.Cleanup
        };
    }

    private static (TimeSeriesIntensiveAcknowledgement? Acknowledgement, TimeSeriesIntensiveFailure Cleanup, long ObservedSequence) Facts(Exception error)
    {
        TimeSeriesIntensiveAcknowledgement? acknowledgement = null;
        TimeSeriesIntensiveFailure cleanup = default;
        while (true)
        {
            switch (error)
            {
                case TimeSeriesIntensiveObservedFailureException { InnerException: { } original }:
                    error = original;
                    break;
                case TimeSeriesIntensiveTargetCompletionException { InnerException: { } original } completed:
                    acknowledgement ??= completed.Acknowledgement;
                    cleanup = cleanup.Origin == TimeSeriesIntensiveFailureOrigin.None ? completed.CleanupFailure : cleanup;
                    error = original;
                    break;
                default:
                    return (acknowledgement, cleanup, (error as KeyLoadTimeSeriesIntensiveReplyException)?.ObservedSequence ?? default);
            }
        }
    }
}
