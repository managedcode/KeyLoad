using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

internal sealed class ScaledStorageReadUnrollConfiguration : ManualConfig
{
    private const int UnrollFactor = 1;

    public ScaledStorageReadUnrollConfiguration()
        => AddJob(Job.Default.WithUnrollFactor(UnrollFactor).AsMutator());
}
