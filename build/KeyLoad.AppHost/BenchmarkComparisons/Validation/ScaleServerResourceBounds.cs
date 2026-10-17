namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class ScaleServerResourceBounds
{
    internal const int MaxContainers = 3;
    internal const int MinimumSamples = 2;
    internal const string HardwareMissing = "hardwareClass";
    internal const string EnvelopeMissing = "effectiveServerResources";
    internal const string StorageMissing = "storageEnvelope";
    internal const string SamplingMissing = "serverCpuRss";
}
