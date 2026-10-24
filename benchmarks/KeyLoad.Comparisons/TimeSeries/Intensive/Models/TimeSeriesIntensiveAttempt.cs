namespace KeyLoad.Comparisons.BenchmarkComparisons.TimeSeries.Intensive;

internal readonly record struct TimeSeriesIntensiveAttempt(int Repetition, int Index, int Worker,
    long LatencyTicks, long ValidationTicks, TimeSeriesIntensiveOutcome Outcome, long? ResultCount,
    TimeSeriesIntensiveHash Digest, long ReceiptSequence, TimeSeriesIntensiveFailure Failure)
{
    internal TimeSeriesIntensiveAcknowledgement? Acknowledgement
    {
        get; init;
    }
    internal TimeSeriesIntensiveFailure CleanupFailure
    {
        get; init;
    }
}
