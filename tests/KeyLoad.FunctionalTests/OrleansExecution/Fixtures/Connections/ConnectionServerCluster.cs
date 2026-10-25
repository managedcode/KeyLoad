namespace KeyLoad.FunctionalTests.OrleansExecution;

/// <summary>Borrows the real Server silo's factory and serializers; it owns no replacement cluster.</summary>
internal sealed class ConnectionServerCluster(Func<RequestCqrsHostedRuntimeFixture> source)
{
    internal IGrainFactory Client => source().Grains;
    internal IServiceProvider ServiceProvider => source().Services;
}
