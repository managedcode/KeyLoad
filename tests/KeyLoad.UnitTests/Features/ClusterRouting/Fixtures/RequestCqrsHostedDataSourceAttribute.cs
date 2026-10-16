namespace KeyLoad.UnitTests.Features.ClusterRouting;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class RequestCqrsHostedDataSourceAttribute(bool nativeTextMaintenance = false,
    bool nativeAnnMaintenance = false) : DataSourceGeneratorAttribute<RequestCqrsHostedRuntimeFixture>
{
    public bool NativeTextMaintenance { get; } = nativeTextMaintenance;
    public bool NativeAnnMaintenance { get; } = nativeAnnMaintenance;

    protected override IEnumerable<Func<RequestCqrsHostedRuntimeFixture>> GenerateDataSources(DataGeneratorMetadata metadata)
    {
        if (NativeTextMaintenance || NativeAnnMaintenance)
        {
            yield return () => new RequestCqrsHostedRuntimeFixture(nativeTextMaintenance: NativeTextMaintenance,
                nativeAnnMaintenance: NativeAnnMaintenance);
            yield break;
        }
        yield return () => SharedDataSources.GetOrCreate<RequestCqrsHostedRuntimeFixture>(
            SharedType.PerTestSession, metadata, null, static () => new RequestCqrsHostedRuntimeFixture());
    }
}
