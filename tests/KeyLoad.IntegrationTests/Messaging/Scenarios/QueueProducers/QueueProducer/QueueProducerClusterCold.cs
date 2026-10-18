using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueProducerClusterCold
{
    internal static async Task RestartAsync(ClusterFixture fixture, CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var node in QueueProducerClusterProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.KillContainerAsync(node, QueueProducerClusterProtocol.ColdScenario, token), failures).ConfigureAwait(false);
        }
        foreach (var node in QueueProducerClusterProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.BeginContainerRestartAsync(node, token), failures).ConfigureAwait(false);
        }
        foreach (var node in QueueProducerClusterProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.RestartContainerAsync(node, token), failures).ConfigureAwait(false);
        }
        foreach (var node in QueueProducerClusterProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(async () => await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(node,
            WaitBehavior.WaitOnResourceUnavailable, token), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
