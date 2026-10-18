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
