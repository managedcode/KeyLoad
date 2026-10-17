using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class MetadataBootstrapFlow
{
    internal static async Task RunAsync(bool conflicting, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var state = new MetadataBootstrapState(conflicting);
            await ServerFailureObserver.ObserveAsync(() => OwnedAsync(state, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task OwnedAsync(MetadataBootstrapState state, CancellationToken token)
    {
        await MetadataBootstrapPolicy.InitializeAsync(state);
        await MetadataBootstrapProducers.RunAsync(state, token);
        var failures = new List<Exception>();
        await MetadataBootstrapPolicy.ConfigureAsync(state, 2L, false);
        await ServerFailureObserver.ObserveAsync(() => MetadataBootstrapPolicy.FenceAsync(state, false), failures);
        await MetadataBootstrapPolicy.ConfigureAsync(state, 3L, true);
        await ServerFailureObserver.ObserveAsync(() => MetadataBootstrapPolicy.FenceAsync(state, true), failures);
        await ServerFailureObserver.ObserveAsync(() => MetadataBootstrapContinuation.HealthyAsync(state, token), failures);
        for (var cold = 0; cold < 2; cold++)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                state.Fixture.Reopen();
                await MetadataBootstrapContinuation.RequireAsync(state, token);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
