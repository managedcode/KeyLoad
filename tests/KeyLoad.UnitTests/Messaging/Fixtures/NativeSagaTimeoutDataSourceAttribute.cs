namespace KeyLoad.UnitTests.Features.Messaging;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class NativeSagaTimeoutDataSourceAttribute : DataSourceGeneratorAttribute<NativeSagaTimeoutFixture>
{
    protected override IEnumerable<Func<NativeSagaTimeoutFixture>> GenerateDataSources(DataGeneratorMetadata metadata)
    {
        yield return () => SharedDataSources.GetOrCreate<NativeSagaTimeoutFixture>(
            SharedType.PerTestSession, metadata, null, static () => new NativeSagaTimeoutFixture());
    }
}
