using KeyLoad.Comparisons;
using Microsoft.Extensions.Options;

namespace KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

internal static class ScaleServerObservationCapture
{
    internal static ScaleServerObservationSnapshot Capture(BenchmarkSessionSelection selection,
        ScaleServerHardware? _hardware, ScaleServerEnvelope? _envelope, ScaleServerResourceSampler[] _samplers,
        HashSet<string> _missing, IOptions<ScaleServerResourceOptions> executionOptions)
    {
        const int EmptyValue = 0;
        if (_hardware is null)
        {
            _missing.Add(ScaleServerResourceBounds.HardwareMissing);
        }
        if (_envelope is null)
        {
            _missing.Add(ScaleServerResourceBounds.EnvelopeMissing);
        }
        var records = _samplers.Select(sampler => (sampler.ToRecord()).Value).Where(record => record is not null)
            .Select(record => record!).ToArray();
        if (records.Length != selection.NodeCount
            || records.Select(record => record.ContainerId).Distinct(StringComparer.Ordinal).Count() != records.Length
            || _samplers.Any(sampler => sampler.Invalidated)
            || records.Any(record => record.SampleCount < ScaleServerResourceBounds.MinimumSamples))
        {
            _missing.Add(ScaleServerResourceBounds.SamplingMissing);
        }
        if (records.Any(record => record.WritableMounts.Length == EmptyValue)
            || _samplers.Any(sampler => sampler.StorageUnavailable))
        {
            _missing.Add(ScaleServerResourceBounds.StorageMissing);
        }
        return new(_hardware, _envelope, records, [.. _missing.Order(StringComparer.Ordinal)],
            _missing.Count == EmptyValue, ScaleServerObservationPolicySnapshot.Capture(executionOptions));
    }
}
