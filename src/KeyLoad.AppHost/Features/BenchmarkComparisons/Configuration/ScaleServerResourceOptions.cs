
namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

/// <summary>Central policy for bounded native server resource observation.</summary>
[ConfigurationOptions]
internal sealed record ScaleServerResourceOptions
{
    private const int NoCapacity = 0;
    private const int MinimumErrorOutputDivisor = 2;
    private const int MaximumErrorOutputDivisor = 4096;
    private const int DefaultErrorOutputDivisor = 8;
    private const int SupportedMaxProcesses = 128;
    private const int SupportedMaxMounts = 8;
    private const int SupportedMaxFileBytes = 4096;
    private const int SupportedMinimumCommandBytes = 8;
    private const int SupportedMaxHardwareBytes = 262144;
    private const int SupportedMaxSampleMetadataBytes = 262144;
    private const int SupportedMaxSidecarBytes = 65536;
    private const int SupportedMaxWorkerBytes = 67108864;
    private const int SupportedMaxSamples = 1680;
    private const int SupportedMaxCgroupAncestors = 64;
    private const int SupportedNativeReadBufferBytes = 4096;
    private const int SupportedMaxNativeOutputBytes = 4096;
    private const int MaximumCadenceMinutes = 1;
    private const int MaximumObservationMinutes = 140;
    private const int MaximumCleanupSeconds = 30;
    internal const string SectionName = "Benchmarks:ServerResources";
    internal const string ValidationMessage = "Server resource observation policy is outside its qualified bounds.";

    public int MaxProcesses { get; init; } = SupportedMaxProcesses;
    public int MaxMounts { get; init; } = SupportedMaxMounts;
    public int MaxFileBytes { get; init; } = SupportedMaxFileBytes;
    public int MinimumCommandBytes { get; init; } = SupportedMinimumCommandBytes;
    public int MaxHardwareBytes { get; init; } = SupportedMaxHardwareBytes;
    public int MaxSampleMetadataBytes { get; init; } = SupportedMaxSampleMetadataBytes;
    public int MaxSidecarBytes { get; init; } = SupportedMaxSidecarBytes;
    public int MaxWorkerBytes { get; init; } = SupportedMaxWorkerBytes;
    public int MaxSamples { get; init; } = SupportedMaxSamples;
    public int MaxCgroupAncestors { get; init; } = SupportedMaxCgroupAncestors;
    public int NativeReadBufferBytes { get; init; } = SupportedNativeReadBufferBytes;
    public int MaxNativeOutputBytes { get; init; } = SupportedMaxNativeOutputBytes;
    public int StandardErrorOutputDivisor { get; init; } = DefaultErrorOutputDivisor;
    public TimeSpan Cadence { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan MaximumObservation { get; init; } = TimeSpan.FromMinutes(140);
    public TimeSpan CleanupThreshold { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan ProcessSettlement { get; init; } = TimeSpan.FromSeconds(1);

    internal bool IsValid() => MaxProcesses is > NoCapacity and <= SupportedMaxProcesses && MaxMounts is > NoCapacity and <= SupportedMaxMounts
        && MaxFileBytes is > NoCapacity and <= SupportedMaxFileBytes && MinimumCommandBytes is >= SupportedMinimumCommandBytes and <= SupportedMaxFileBytes
        && MinimumCommandBytes <= MaxFileBytes && MaxHardwareBytes is > NoCapacity and <= SupportedMaxHardwareBytes
        && MaxSampleMetadataBytes is > NoCapacity and <= SupportedMaxSampleMetadataBytes && MaxHardwareBytes <= MaxSampleMetadataBytes
        && MaxSidecarBytes is > NoCapacity and <= SupportedMaxSidecarBytes && MaxWorkerBytes is > NoCapacity and <= SupportedMaxWorkerBytes
        && MaxSamples is >= ScaleServerResourceBounds.MinimumSamples and <= SupportedMaxSamples
        && MaxCgroupAncestors is > NoCapacity and <= SupportedMaxCgroupAncestors && NativeReadBufferBytes is > NoCapacity and <= SupportedNativeReadBufferBytes
        && MaxNativeOutputBytes is > NoCapacity and <= SupportedMaxNativeOutputBytes && NativeReadBufferBytes <= MaxFileBytes
        && StandardErrorOutputDivisor is >= MinimumErrorOutputDivisor and <= MaximumErrorOutputDivisor
        && Positive(Cadence, MaximumCadenceMinutes * TimeSpan.TicksPerMinute) && Positive(MaximumObservation, MaximumObservationMinutes * TimeSpan.TicksPerMinute)
        && Cadence < MaximumObservation && Positive(CleanupThreshold, MaximumCleanupSeconds * TimeSpan.TicksPerSecond)
        && Positive(ProcessSettlement, MaximumCleanupSeconds * TimeSpan.TicksPerSecond);

    internal void Validate()
    {
        if (!IsValid())
        { throw new ArgumentException(ValidationMessage); }
    }

    private static bool Positive(TimeSpan value, long maximumTicks) => value > TimeSpan.Zero && value.Ticks <= maximumTicks;
}
