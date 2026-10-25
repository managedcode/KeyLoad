using Aspire.Hosting.ApplicationModel;
using KeyLoad.ClusterTests.StorageReplication;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.Authorization;

internal static class QueryOnlyEpochR46Cold
{
    internal static async Task RestartAsync(ClusterFixture fixture, CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var node in PolicyEpochPublicProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.KillServerAsync(node,
                QueryOnlyEpochR46Protocol.ColdScenario, token), failures);
        }
        foreach (var node in PolicyEpochPublicProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.BeginProcessRestartAsync(node, token), failures);
        }
        foreach (var node in PolicyEpochPublicProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.RestartServerAsync(node, token), failures);
        }
        foreach (var node in PolicyEpochPublicProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(
                node, WaitBehavior.WaitOnResourceUnavailable, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
