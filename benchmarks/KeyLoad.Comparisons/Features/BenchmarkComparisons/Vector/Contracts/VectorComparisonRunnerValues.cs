namespace KeyLoad.Comparisons;

internal static class VectorComparisonRunnerValues
{
    internal const string DoesNotImplement = " does not implement ";
    internal const string AlgorithmModeSeparator = "/";
    internal const string Natively = " natively.";
    internal const string NativeIngestionLoaded = "Native ingestion loaded ";
    internal const string RecordsExpected = " records; expected ";
    internal const string SentencePeriod = ".";
    internal const int FirstIndex = 0;
    internal const string TheNativeOperationTimeoutMustEqual = "The native operation timeout must equal the immutable vector profile.";
    internal const string NativeIndexReceiptDoesNotMatch = "Native index receipt does not match the selected profile.";
    internal const string NativeQueryPlanIsEmpty = "Native query plan is empty.";
    internal const string TheMeasuredPostgreSQLQueryPlanDoes = "The measured PostgreSQL query plan does not prove the requested ANN index is used.";
    internal const string NativeANNDidNotProvideItsActual = "NativeANN did not provide its actual algorithm configuration.";
    internal const double P95Fraction = 0.95;
    internal const double P99Fraction = 0.99;
    internal const int ReportSchemaVersion = 3;
    internal const string ClosedLoopVectorQueryThroughputIncludes = "closed-loop vector query throughput includes neighbor validation; update duration includes post-ack native readback and validation; ingestion, oracle, index build and warmup excluded";
    internal const int SingleElementOffset = 1;
}
