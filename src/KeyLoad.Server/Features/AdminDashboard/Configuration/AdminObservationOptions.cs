using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Bounds node-local observation without changing storage ownership or durable state.</summary>
[ConfigurationOptions]
internal sealed class AdminObservationOptions
{
    internal const string SectionName = "KeyLoad:AdminObservation";
    internal const string ValidationMessage = "Administrative observation limits must be positive and within their accepted ceilings.";
    private const int MinimumPositiveCount = 1;
    private const int MaximumEntryCeiling = 2_048;
    private const int MaximumRetainedFileCeiling = 200;
    private const int MaximumScanMilliseconds = 250;
    private static readonly TimeSpan MaximumScanDeadline = TimeSpan.FromMilliseconds(MaximumScanMilliseconds);

    public int MaximumEntries { get; set; } = MaximumEntryCeiling;
    public int MaximumRetainedFiles { get; set; } = MaximumRetainedFileCeiling;
    public TimeSpan ScanDeadline { get; set; } = TimeSpan.FromMilliseconds(MaximumScanMilliseconds);

    internal bool IsValid() => MaximumEntries is >= MinimumPositiveCount and <= MaximumEntryCeiling
        && MaximumRetainedFiles is >= MinimumPositiveCount and <= MaximumRetainedFileCeiling
        && MaximumRetainedFiles <= MaximumEntries
        && ScanDeadline > TimeSpan.Zero && ScanDeadline <= MaximumScanDeadline;

    internal void Validate()
    {
        if (!IsValid())
        { throw new OptionsValidationException(SectionName, typeof(AdminObservationOptions), [ValidationMessage]); }
    }
}
