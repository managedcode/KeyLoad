namespace KeyLoad.UnitTests.Features.ClusterRouting;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class RequestCqrsDataSourceAttribute(bool nativeTextMaintenance = false, bool nativeAnnMaintenance = false)
    : DataSourceGeneratorAttribute<RequestCqrsClusterFixture>
{
    public bool NativeTextMaintenance { get; } = nativeTextMaintenance;
    public bool NativeAnnMaintenance { get; } = nativeAnnMaintenance;

    protected override IEnumerable<Func<RequestCqrsClusterFixture>> GenerateDataSources(DataGeneratorMetadata metadata)
    {
        if (NativeAnnMaintenance)
        {
            yield return static () => new RequestCqrsClusterFixture(nativeAnnMaintenance:
                static database => new KeyLoad.UnitTests.Features.Search.NativeAnnMaintenanceTestRuntime(database));
            yield break;
        }
        if (NativeTextMaintenance)
        {
            yield return static () => new RequestCqrsClusterFixture(nativeMaintenance:
                static database => new KeyLoad.UnitTests.Features.Search.NativeTextMaintenanceTestRuntime(database));
            yield break;
        }
        yield return () => SharedDataSources.GetOrCreate<RequestCqrsClusterFixture>(
            SharedType.PerTestSession, metadata, null, static () => new RequestCqrsClusterFixture());
    }
}
