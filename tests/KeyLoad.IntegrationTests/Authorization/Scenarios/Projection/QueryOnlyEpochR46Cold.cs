using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class QueryOnlyEpochR46Cold
{
    internal static async Task RestartAsync(ClusterFixture fixture, CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var node in PolicyEpochPublicProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.KillContainerAsync(node,
                QueryOnlyEpochR46Protocol.ColdScenario, token), failures);
        }
        foreach (var node in PolicyEpochPublicProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.BeginContainerRestartAsync(node, token), failures);
        }
        foreach (var node in PolicyEpochPublicProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.RestartContainerAsync(node, token), failures);
        }
        foreach (var node in PolicyEpochPublicProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(
                node, WaitBehavior.WaitOnResourceUnavailable, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
