using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class MetadataBootstrapRf3Flow
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = new MetadataBootstrapRf3State(fixture);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await MetadataBootstrapRf3Setup.RunAsync(state, token);
            await MetadataBootstrapRf3Failover.RunAsync(state, token);
        }, failures);
        using var cleanup = new CancellationTokenSource(RequestCqrsRf3Protocol.CleanupDeadline, TimeProvider.System);
        await ServerFailureObserver.ObserveAsync(() => MetadataBootstrapRf3Failover.RestoreAsync(state, cleanup.Token), failures);
        await ServerFailureObserver.ObserveAsync(() => MetadataBootstrapRf3Wave.RunAsync(state, token), failures);
        for (var cold = 0; cold < 2; cold++)
        {
            await ServerFailureObserver.ObserveAsync(() => state.Cold.RunAsync(token), failures);
            await ServerFailureObserver.ObserveAsync(() => MetadataBootstrapRf3Wave.RunAsync(state, token), failures);
        }
        await ServerFailureObserver.ObserveAsync(() => state.Cold.RequireAsync(cleanup.Token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
