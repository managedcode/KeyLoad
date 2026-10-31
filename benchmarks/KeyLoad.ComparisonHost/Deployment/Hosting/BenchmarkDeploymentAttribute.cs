namespace KeyLoad.ComparisonHost.Deployment;

/// <summary>Gives every case class of the job the same <see cref="BenchmarkDeployment"/>, shared for the whole test session.</summary>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class BenchmarkDeploymentAttribute : DataSourceGeneratorAttribute<BenchmarkDeployment>
{
    protected override IEnumerable<Func<BenchmarkDeployment>> GenerateDataSources(DataGeneratorMetadata metadata)
    {
        yield return () => SharedDataSources.GetOrCreate(SharedType.PerTestSession, metadata, key: null,
            factory: static () => new BenchmarkDeployment());
    }
}
