using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.Features.BenchmarkOrchestration;

[ConfigurationOptions]
internal sealed partial record ComparisonHarnessOptions
{
    internal const string SectionName = "KeyLoad:ComparisonHarness";
    internal const string ValidationMessage = "Execution comparison policy is outside its supported bounds.";
    internal const int PositiveMinimum = 1;
    private const int SettlementSeconds = 30, ReceiptSeconds = 30, ReceiptFileBufferBytes = 4096;
    private const int ProgressWriteSeconds = 5, ProgressBufferBytes = 512, LatestProgressMarkers = 1;

    public TimeSpan OriginalTaskSettlementTimeout { get; init; } = TimeSpan.FromSeconds(SettlementSeconds);
    public TimeSpan TeardownReceiptTimeout { get; init; } = TimeSpan.FromSeconds(ReceiptSeconds);
    public int TeardownReceiptFileBufferBytes { get; init; } = ReceiptFileBufferBytes;
    public TimeSpan ProgressWriteTimeout { get; init; } = TimeSpan.FromSeconds(ProgressWriteSeconds);
    public int ProgressFileBufferBytes { get; init; } = ProgressBufferBytes;
    public int ProgressMarkerCapacity { get; init; } = LatestProgressMarkers;

    internal void Validate()
    {
        if (!IsValid())
        {
            throw new OptionsValidationException(SectionName, typeof(ComparisonHarnessOptions), [ValidationMessage]);
        }
    }

    internal bool IsValid()
        => PositiveSeconds(OriginalTaskSettlementTimeout, SettlementSeconds)
            && PositiveSeconds(TeardownReceiptTimeout, ReceiptSeconds)
            && TeardownReceiptFileBufferBytes is >= PositiveMinimum and <= ReceiptFileBufferBytes
            && PositiveSeconds(ProgressWriteTimeout, ProgressWriteSeconds)
            && ProgressFileBufferBytes is >= PositiveMinimum and <= ProgressBufferBytes
            && ProgressMarkerCapacity == LatestProgressMarkers && IsLogPolicyValid();

    private static bool PositiveSeconds(TimeSpan value, int ceilingSeconds)
        => value > TimeSpan.Zero && value.Ticks <= ceilingSeconds * TimeSpan.TicksPerSecond;
}
