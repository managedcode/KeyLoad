using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.StorageReplication;
using KeyLoad.Server;
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class InboxProcessingClusterCold
{
    internal static async Task RestartAsync(ClusterFixture fixture, CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var node in InboxProcessingClusterProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.KillServerAsync(node, InboxProcessingClusterProtocol.Cold, token), failures).ConfigureAwait(false);
        }
        foreach (var node in InboxProcessingClusterProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.BeginProcessRestartAsync(node, token), failures).ConfigureAwait(false);
        }
        foreach (var node in InboxProcessingClusterProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.RestartServerAsync(node, token), failures).ConfigureAwait(false);
        }
        foreach (var node in InboxProcessingClusterProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(async () => await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(
            node, WaitBehavior.WaitOnResourceUnavailable, token), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
