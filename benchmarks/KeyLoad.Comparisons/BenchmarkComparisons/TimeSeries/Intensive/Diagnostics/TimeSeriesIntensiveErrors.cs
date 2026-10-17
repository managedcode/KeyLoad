namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveErrors
{
    internal const string UnknownSeries = "Unknown intensive series.";
    internal const string InvalidSeedReadback = "Invalid intensive seed readback.";
    internal const string SampleCardinality = "Intensive sample cardinality mismatch.";
    internal const string WindowCardinality = "Intensive window cardinality mismatch.";
    internal const string WindowBoundary = "Intensive window boundary/order mismatch.";
    internal const string WholeSeriesCount = "Intensive whole-series count mismatch.";
    internal const string QuarterValues = "Oracle requires exact quarter-unit values.";
    internal const string NonfiniteStatistics = "Intensive statistics are missing or nonfinite.";
    internal const string Statistics = "Intensive statistics mismatch.";
    internal const string Sample = "Intensive sample identity/value/order mismatch.";
    internal const string ReceiptCardinality = "Intensive receipt cardinality mismatch.";
    internal const string ReceiptIdentity = "Intensive receipt identity/sequence mismatch.";
    internal const string DuplicateReceipt = "Intensive receipt sequence duplicated.";
    internal const string InvalidAppendReadback = "Invalid intensive append readback.";
    internal const string InvalidReadbackReceipt = "Intensive readback receipt missing or invalid.";
    internal const string UninitializedSamples = "Uninitialized sample array.";
    internal const string UninitializedWindows = "Uninitialized window array.";
}
