namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal enum TimeSeriesIntensiveOutcome
{
    NotStarted,
    Succeeded,
    TargetFailure,
    TransportFailure,
    DeadlineExceeded,
    Cancelled,
    ValidationFailure,
    UnexpectedFailure
}

internal readonly record struct TimeSeriesIntensiveAttempt(int Repetition, int Index, int Worker,
    long LatencyTicks, long ValidationTicks, TimeSeriesIntensiveOutcome Outcome, long? ResultCount,
    TimeSeriesIntensiveHash Digest, long ReceiptSequence, TimeSeriesIntensiveFailure Failure);
