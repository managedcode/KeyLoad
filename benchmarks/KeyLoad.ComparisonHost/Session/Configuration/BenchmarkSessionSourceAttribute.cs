namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Uses TUnit sharing and lifecycle with an explicit workload owner constructor.</summary>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class BenchmarkSessionSourceAttribute : DataSourceGeneratorAttribute<BenchmarkSessionFixture>
{
    protected override IEnumerable<Func<BenchmarkSessionFixture>> GenerateDataSources(DataGeneratorMetadata metadata)
    {
        yield return () => SharedDataSources.GetOrCreate<BenchmarkSessionFixture>(SharedType.PerTestSession,
            metadata, key: null, factory: static () => new BenchmarkSessionFixture());
    }
}
