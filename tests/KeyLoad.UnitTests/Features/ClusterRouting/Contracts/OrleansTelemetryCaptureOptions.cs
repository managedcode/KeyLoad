namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Bounds retained telemetry records in the native exporter sink used by this test fixture.</summary>
[KeyLoad.ConfigurationOptions]
internal sealed class OrleansTelemetryCaptureOptions
{
    internal const string ValidationMessage = "The native telemetry capture capacity must be between one and one thousand records.";
    private const int DefaultMaximumRecords = 256;
    private const int MaximumAllowedRecords = 1_000;

    public int MaximumRecords { get; set; } = DefaultMaximumRecords;

    public bool IsValid() => MaximumRecords is >= 1 and <= MaximumAllowedRecords;
}
