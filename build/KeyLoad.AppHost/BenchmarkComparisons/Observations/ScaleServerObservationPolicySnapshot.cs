using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class ScaleServerObservationPolicySnapshot
{
    internal static ScaleServerObservationPolicy Capture(IOptions<ScaleServerResourceOptions> options)
    {
        var value = options.Value;
        value.Validate();
        return new(value.Cadence.TotalMilliseconds, value.MaximumObservation.TotalMilliseconds,
            value.CleanupThreshold.TotalMilliseconds, value.ProcessSettlement.TotalMilliseconds,
            value.MaxProcesses, value.MaxMounts, value.MaxFileBytes, value.MinimumCommandBytes, value.MaxHardwareBytes,
            value.MaxSampleMetadataBytes, value.MaxSidecarBytes, value.MaxWorkerBytes, value.MaxSamples,
            value.MaxCgroupAncestors, value.NativeReadBufferBytes, value.MaxNativeOutputBytes, value.StandardErrorOutputDivisor);
    }
}
