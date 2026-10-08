namespace KeyLoad.UnitTests.Features.Authorization;

[ConfigurationOptions]
internal sealed class AuthorizationTelemetryCaptureOptions
{
    private const int RecordCeiling = 512;
    private const int TimeoutMilliseconds = 10_000;
    private const int PollMilliseconds = 10;
    private const int IntervalMilliseconds = 60_000;
    internal int PollIntervalMilliseconds { get; set; } = PollMilliseconds;
    internal int MaximumRecords { get; set; } = RecordCeiling;
    internal int FlushTimeoutMilliseconds { get; set; } = TimeoutMilliseconds;
    internal int ExportIntervalMilliseconds { get; set; } = IntervalMilliseconds;
    internal bool IsValid() => MaximumRecords is > 0 and <= RecordCeiling
        && FlushTimeoutMilliseconds is > 0 and <= TimeoutMilliseconds
        && ExportIntervalMilliseconds >= FlushTimeoutMilliseconds
        && PollIntervalMilliseconds is > 0 and <= PollMilliseconds;
}
