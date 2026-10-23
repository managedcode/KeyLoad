using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

/// <summary>Provides the reflection-created BenchmarkDotNet loop policy for raw storage.</summary>
public sealed class RawStorageUnrollConfiguration : ManualConfig
{
    /// <summary>Keeps the frozen default loop unroll without overriding the requested invocation count.</summary>
    public RawStorageUnrollConfiguration()
        => AddJob(Job.Default.WithUnrollFactor(1).AsMutator());
}
