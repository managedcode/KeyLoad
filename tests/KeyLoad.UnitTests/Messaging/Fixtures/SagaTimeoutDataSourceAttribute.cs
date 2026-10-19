namespace KeyLoad.UnitTests.Features.Messaging;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class SagaTimeoutDataSourceAttribute : DataSourceGeneratorAttribute<SagaTimeoutFixture>
{
    protected override IEnumerable<Func<SagaTimeoutFixture>> GenerateDataSources(DataGeneratorMetadata metadata)
    {
        yield return () => SharedDataSources.GetOrCreate<SagaTimeoutFixture>(
            SharedType.PerTestSession, metadata, null, static () => new SagaTimeoutFixture());
    }
}
