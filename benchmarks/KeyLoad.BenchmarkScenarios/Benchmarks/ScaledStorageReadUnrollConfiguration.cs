using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

/// <summary>Provides the reflection-created BenchmarkDotNet loop policy for scaled reads.</summary>
public sealed class ScaledStorageReadUnrollConfiguration : ManualConfig
{
    private const int UnrollFactor = 1;

    /// <summary>Keeps one unrolled operation without changing the selected invocation count.</summary>
    public ScaledStorageReadUnrollConfiguration()
        => AddJob(Job.Default.WithUnrollFactor(UnrollFactor).AsMutator());
}
