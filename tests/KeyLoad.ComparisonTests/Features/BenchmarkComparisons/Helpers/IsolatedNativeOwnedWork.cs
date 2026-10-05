using KeyLoad.AppHost.Features.BenchmarkComparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedNativeOwnedWork
{
    internal ScaleServerResourceEvidenceCollector? Collector { get; set; }
    internal Task? Observation { get; set; }
    internal Task? CollectorSettlement { get; private set; }

    internal Task StartSettlement()
    {
        if (Collector is null || Observation is null)
            throw new InvalidOperationException("Server resource observation is not owned.");
        return CollectorSettlement ??= Collector.CompleteAsync(Observation);
    }
}
