namespace KeyLoad.UnitTests.OrleansExecution;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class CqrsDataSourceAttribute : DataSourceGeneratorAttribute<CqrsClusterFixture>
{
    public CqrsDataSourceAttribute()
    {
    }

    protected override IEnumerable<Func<CqrsClusterFixture>> GenerateDataSources(
        DataGeneratorMetadata metadata)
    {
        yield return () => SharedDataSources.GetOrCreate<CqrsClusterFixture>(
            SharedType.PerTestSession,
            metadata,
            null,
            static () => new CqrsClusterFixture());
    }
}
