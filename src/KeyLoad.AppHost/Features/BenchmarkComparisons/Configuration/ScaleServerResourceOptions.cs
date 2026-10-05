using KeyLoad;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

/// <summary>Central policy for bounded native server resource observation.</summary>
[ConfigurationOptions]
internal sealed record ScaleServerResourceOptions
{
    private const int MaximumCadenceMinutes = 1;
    private const int MaximumObservationMinutes = 140;
    private const int MaximumCleanupSeconds = 30;
    internal const string SectionName = "Benchmarks:ServerResources";
    internal const string ValidationMessage = "Server resource observation policy is outside its qualified bounds.";

    public int MaxProcesses { get; init; } = 128;
    public int MaxMounts { get; init; } = 8;
    public int MaxFileBytes { get; init; } = 4096;
    public int MinimumCommandBytes { get; init; } = 8;
    public int MaxHardwareBytes { get; init; } = 262144;
    public int MaxSampleMetadataBytes { get; init; } = 262144;
    public int MaxSidecarBytes { get; init; } = 65536;
    public int MaxWorkerBytes { get; init; } = 67108864;
    public int MaxSamples { get; init; } = 1680;
    public int MaxCgroupAncestors { get; init; } = 64;
    public int NativeReadBufferBytes { get; init; } = 4096;
    public int MaxNativeOutputBytes { get; init; } = 4096;
    public TimeSpan Cadence { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan MaximumObservation { get; init; } = TimeSpan.FromMinutes(140);
    public TimeSpan CleanupThreshold { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan ProcessSettlement { get; init; } = TimeSpan.FromSeconds(1);

    internal bool IsValid() => MaxProcesses is > 0 and <= 128 && MaxMounts is > 0 and <= 8
        && MaxFileBytes is > 0 and <= 4096 && MinimumCommandBytes is >= 8 and <= 4096
        && MinimumCommandBytes <= MaxFileBytes && MaxHardwareBytes is > 0 and <= 262144
        && MaxSampleMetadataBytes is > 0 and <= 262144 && MaxHardwareBytes <= MaxSampleMetadataBytes
        && MaxSidecarBytes is > 0 and <= 65536 && MaxWorkerBytes is > 0 and <= 67108864
        && MaxSamples is >= ScaleServerResourceBounds.MinimumSamples and <= 1680
        && MaxCgroupAncestors is > 0 and <= 64 && NativeReadBufferBytes is > 0 and <= 4096
        && MaxNativeOutputBytes is > 0 and <= 4096 && NativeReadBufferBytes <= MaxFileBytes
        && Positive(Cadence, MaximumCadenceMinutes * TimeSpan.TicksPerMinute) && Positive(MaximumObservation, MaximumObservationMinutes * TimeSpan.TicksPerMinute)
        && Cadence < MaximumObservation && Positive(CleanupThreshold, MaximumCleanupSeconds * TimeSpan.TicksPerSecond)
        && Positive(ProcessSettlement, MaximumCleanupSeconds * TimeSpan.TicksPerSecond);

    internal void Validate()
    {
        if (!IsValid()) { throw new ArgumentException(ValidationMessage); }
    }

    private static bool Positive(TimeSpan value, long maximumTicks) => value > TimeSpan.Zero && value.Ticks <= maximumTicks;
}
