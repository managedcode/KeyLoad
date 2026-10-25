namespace KeyLoad.FunctionalTests.OrleansExecution;

/// <summary>Bounds retained telemetry records in the execution exporter sink used by this test fixture.</summary>
[KeyLoad.ConfigurationOptions]
internal sealed class OrleansTelemetryCaptureOptions
{
    internal const string ValidationMessage = "Execution telemetry capture and flush bounds are outside their accepted range.";
    internal const int DefaultMaximumRecords = 256;
    internal const int DefaultFlushTimeoutMilliseconds = 10_000;
    private const int MaximumAllowedRecords = 1_000;
    private const int MinimumFlushTimeoutMilliseconds = 1_000;
    private const int MaximumFlushTimeoutMilliseconds = 30_000;

    public int MaximumRecords { get; set; } = DefaultMaximumRecords;

    public int FlushTimeoutMilliseconds { get; set; } = DefaultFlushTimeoutMilliseconds;

    public bool IsValid() => MaximumRecords >= 1 && MaximumRecords <= MaximumAllowedRecords
        && FlushTimeoutMilliseconds is >= MinimumFlushTimeoutMilliseconds and <= MaximumFlushTimeoutMilliseconds;
}
