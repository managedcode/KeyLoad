namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Borrows the real Server silo's factory and serializers; it owns no replacement cluster.</summary>
internal sealed class ConnectionNativeServerCluster(Func<RequestCqrsHostedRuntimeFixture> source)
{
    internal IGrainFactory Client => source().Grains;
    internal IServiceProvider ServiceProvider => source().Services;
}
