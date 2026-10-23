namespace KeyLoad.UnitTests.Features.CodeQuality;

/// <summary>Owns the bounded snapshot passed to execution CLI deployment and original collector settlement.</summary>
[ConfigurationOptions]
internal sealed class CoverageExecutionOptions
{
    internal const string SectionName = "KeyLoadTests:RuntimeCoverage";
    internal const string ValidationMessage = "Execution functional coverage limits or settlement durations are invalid.";
    private const int MinimumClosureFiles = 2;
    private const int DefaultDescriptorBytes = 65_536;
    private const int DefaultMaximumFiles = 4_096;
    private const int DefaultReadBufferBytes = 65_536;
    private const int FormatMaximumReadBufferBytes = 1_048_576;
    private const int FormatMaximumFiles = 65_536;
    private const long DefaultTotalBytes = 2L * 1_024 * 1_024 * 1_024;
    private const long FormatMaximumTotalBytes = 16L * 1_024 * 1_024 * 1_024;
    private const int DefaultFileBytes = 256 * 1_024 * 1_024;
    private const int FormatMaximumFileBytes = 1_024 * 1_024 * 1_024;
    private const int DefaultManifestBytes = 16 * 1_024 * 1_024;
    private const int FormatMaximumManifestBytes = 64 * 1_024 * 1_024;
    private const int DefaultReportBytes = 256 * 1_024 * 1_024;
    private const int FormatMaximumReportBytes = 512 * 1_024 * 1_024;
    private const int DefaultShutdownSeconds = 10;
    private const int DefaultSettlementSeconds = 20;
    private const int DefaultProcessStopSeconds = 45;
    private const int DefaultApplicationCleanupSeconds = 180;
    private const int MaximumDurationSeconds = 600;
    private const int ClusterNodeCount = 3;

    public int MaximumDescriptorBytes { get; set; } = DefaultDescriptorBytes;
    public int MaximumFiles { get; set; } = DefaultMaximumFiles;
    public int ReadBufferBytes { get; set; } = DefaultReadBufferBytes;
    public long MaximumTotalBytes { get; set; } = DefaultTotalBytes;
    public int MaximumFileBytes { get; set; } = DefaultFileBytes;
    public int MaximumPathCharacters { get; set; } = CoverageProtocol.MaximumPathFormatCharacters;
    public int MaximumManifestBytes { get; set; } = DefaultManifestBytes;
    public int MaximumReportBytes { get; set; } = DefaultReportBytes;
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(DefaultShutdownSeconds);
    public TimeSpan SettlementTimeout { get; set; } = TimeSpan.FromSeconds(DefaultSettlementSeconds);
    public TimeSpan ProcessStopTimeout { get; set; } = TimeSpan.FromSeconds(DefaultProcessStopSeconds);
    public TimeSpan ApplicationCleanupTimeout { get; set; } = TimeSpan.FromSeconds(DefaultApplicationCleanupSeconds);

    internal bool IsValid() => MaximumDescriptorBytes is >= 1 and <= CoverageProtocol.MaximumDescriptorFormatBytes
        && MaximumFiles is >= MinimumClosureFiles and <= FormatMaximumFiles
        && ReadBufferBytes is >= 1 and <= FormatMaximumReadBufferBytes
        && ReadBufferBytes <= MaximumFileBytes
        && MaximumTotalBytes is >= 1 and <= FormatMaximumTotalBytes
        && MaximumFileBytes is >= 1 and <= FormatMaximumFileBytes
        && MaximumFileBytes <= MaximumTotalBytes
        && MaximumPathCharacters is >= 1 and <= CoverageProtocol.MaximumPathFormatCharacters
        && MaximumManifestBytes is >= 1 and <= FormatMaximumManifestBytes
        && MaximumManifestBytes <= MaximumFileBytes
        && MaximumReportBytes is >= 1 and <= FormatMaximumReportBytes
        && WholeSeconds(ShutdownTimeout) && WholeSeconds(SettlementTimeout)
        && WholeSeconds(ProcessStopTimeout) && WholeSeconds(ApplicationCleanupTimeout)
        && ShutdownTimeout + SettlementTimeout < ProcessStopTimeout
        && ApplicationCleanupTimeout.Ticks > ClusterNodeCount * ProcessStopTimeout.Ticks;

    private static bool WholeSeconds(TimeSpan value) => value > TimeSpan.Zero
        && value.Ticks <= MaximumDurationSeconds * TimeSpan.TicksPerSecond
        && value.Ticks % TimeSpan.TicksPerSecond == TimeSpan.Zero.Ticks;
}
