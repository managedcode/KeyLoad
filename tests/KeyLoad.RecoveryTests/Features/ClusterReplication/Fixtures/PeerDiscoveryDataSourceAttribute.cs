namespace KeyLoad.RecoveryTests;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class PeerDiscoveryDataSourceAttribute : DataSourceGeneratorAttribute<PeerDiscoveryNativeCluster>
{
    protected override IEnumerable<Func<PeerDiscoveryNativeCluster>> GenerateDataSources(DataGeneratorMetadata metadata)
    {
        yield return () => SharedDataSources.GetOrCreate<PeerDiscoveryNativeCluster>(SharedType.PerTestSession,
            metadata, null, static () => new PeerDiscoveryNativeCluster());
    }
}
