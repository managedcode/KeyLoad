using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

internal sealed class RawStorageUnrollConfiguration : ManualConfig
{
    private const int CtorFactorSingleItemCount = 1;

    /// <summary>Keeps the frozen default loop unroll without overriding the requested invocation count.</summary>
    public RawStorageUnrollConfiguration()
        => AddJob(Job.Default.WithUnrollFactor(CtorFactorSingleItemCount).AsMutator());
}
