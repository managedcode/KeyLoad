namespace KeyLoad.UnitTests.Features.ClusterRouting;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class NativeCqrsDataSourceAttribute : DataSourceGeneratorAttribute<NativeCqrsClusterFixture>
{
    public NativeCqrsDataSourceAttribute()
    {
    }

    protected override IEnumerable<Func<NativeCqrsClusterFixture>> GenerateDataSources(
        DataGeneratorMetadata metadata)
    {
        yield return () => SharedDataSources.GetOrCreate<NativeCqrsClusterFixture>(
            SharedType.PerTestSession,
            metadata,
            null,
            static () => new NativeCqrsClusterFixture());
    }
}
