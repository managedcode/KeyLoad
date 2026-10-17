namespace KeyLoad.UnitTests.Features.ClusterReplication;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class RequestCqrsCohortDataSourceAttribute
    : DataSourceGeneratorAttribute<RequestCqrsCohortRuntimeFixture>
{
    protected override IEnumerable<Func<RequestCqrsCohortRuntimeFixture>> GenerateDataSources(
        DataGeneratorMetadata metadata)
    {
        yield return () => SharedDataSources.GetOrCreate<RequestCqrsCohortRuntimeFixture>(
            SharedType.PerTestSession,
            metadata,
            null,
            static () => new RequestCqrsCohortRuntimeFixture());
    }
}
