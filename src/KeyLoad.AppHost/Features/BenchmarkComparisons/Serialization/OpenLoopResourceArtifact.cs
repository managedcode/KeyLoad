using KeyLoad.Comparisons;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal sealed record OpenLoopResourceArtifact(string Kind, string Name, string Sha256,
    int OfferedRatePerSecond, IsolatedComparisonWorker Worker);
