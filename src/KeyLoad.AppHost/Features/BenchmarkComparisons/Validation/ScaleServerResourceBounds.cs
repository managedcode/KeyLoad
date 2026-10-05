namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class ScaleServerResourceBounds
{
    internal const int MaxContainers = 3;
    internal const int MaxProcesses = 128;
    internal const int MaxMounts = 8;
    internal const int MaxFileBytes = 4096;
    internal const int MinimumCommandBytes = 8;
    internal const int MaxHardwareBytes = 262144;
    internal const int MaxSampleMetadataBytes = 262144;
    internal const int MaxSidecarBytes = 65536;
    internal const int MaxWorkerBytes = 67108864;
    internal const int MaxSamples = 1680;
    internal const int MinimumSamples = 2;
    internal const int MaxCgroupAncestors = 64;
    internal const int CadenceSeconds = 5;
    internal const int MaxObservationMinutes = 140;
    internal const int CleanupSeconds = 30;
    internal const string HardwareMissing = "hardwareClass";
    internal const string EnvelopeMissing = "effectiveServerResources";
    internal const string StorageMissing = "storageEnvelope";
    internal const string SamplingMissing = "serverCpuRss";
}
