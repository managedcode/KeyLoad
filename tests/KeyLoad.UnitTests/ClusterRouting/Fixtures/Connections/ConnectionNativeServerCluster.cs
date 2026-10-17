using KeyLoad.CrashHost.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Borrows the real Server silo's factory and serializers; it owns no replacement cluster.</summary>
internal sealed class ConnectionNativeServerCluster(Func<ControlledPartitionMovementNativeHost> source)
{
    internal IGrainFactory Client => source().Silo.Grains
        ?? throw new InvalidOperationException(ConnectionNativeServerRuntime.NotStarted);
    internal IServiceProvider ServiceProvider => source().Application.Services;
}
