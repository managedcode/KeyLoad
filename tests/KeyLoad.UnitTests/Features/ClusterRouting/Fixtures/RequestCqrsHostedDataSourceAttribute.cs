namespace KeyLoad.UnitTests.Features.ClusterRouting;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class RequestCqrsHostedDataSourceAttribute(bool nativeTextMaintenance = false,
    bool nativeAnnMaintenance = false) : DataSourceGeneratorAttribute<RequestCqrsHostedRuntimeFixture>
{
    protected override IEnumerable<Func<RequestCqrsHostedRuntimeFixture>> GenerateDataSources(DataGeneratorMetadata metadata)
    {
        if (nativeTextMaintenance || nativeAnnMaintenance)
        {
            yield return () => new RequestCqrsHostedRuntimeFixture(nativeTextMaintenance: nativeTextMaintenance,
                nativeAnnMaintenance: nativeAnnMaintenance);
            yield break;
        }
        yield return () => SharedDataSources.GetOrCreate<RequestCqrsHostedRuntimeFixture>(
            SharedType.PerTestSession, metadata, null, static () => new RequestCqrsHostedRuntimeFixture());
    }
}
