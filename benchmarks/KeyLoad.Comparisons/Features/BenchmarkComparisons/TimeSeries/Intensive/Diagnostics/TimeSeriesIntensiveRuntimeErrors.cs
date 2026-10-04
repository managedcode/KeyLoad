namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveRuntimeErrors
{
    internal const string InvalidHash = "An intensive result digest must contain exactly 32 hexadecimal bytes.";
    internal const string InvalidSqlState = "A native SQLSTATE must contain five uppercase ASCII letters or digits.";
    internal const string InvalidAttempt = "An intensive attempt has invalid identity, outcome or durations.";
    internal const string DuplicateAttempt = "An intensive attempt was published more than once.";
    internal const string MissingAttempt = "An intensive phase has missing attempt publications.";
    internal const string InvalidSummary = "An intensive summary requires complete attempts with valid durations.";
    internal const string InvalidMedian = "An intensive median requires five finite nonnegative summaries.";
    internal const string InvalidPercentile = "An intensive percentile requires nonnegative values and a valid rank.";
    internal const string InvalidReceipt = "An intensive append receipt does not match its command or sequence bounds.";
    internal const string DuplicateSequence = "An intensive append sequence was acknowledged more than once.";
    internal const string MissingSequence = "An intensive append phase does not contain the complete sequence bijection.";
    internal const string UnvalidatedReceipt = "An intensive readback cannot reconstruct an unvalidated receipt.";
    internal const string CallDeadline = "An intensive operation completed after its deadline.";
}
