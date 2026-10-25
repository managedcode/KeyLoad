using Aspire.Hosting.ApplicationModel;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.StorageReplication;
using KeyLoad.Server;
using static KeyLoad.ClusterTests.EventStreams.SharedDomainPublicProtocol;

namespace KeyLoad.ClusterTests.EventStreams;

internal static class SharedDomainPublicCold
{
    internal static async Task RestartAsync(ClusterFixture fixture, CancellationToken token)
    {
        var failures = new List<Exception>();
        string[] nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];
        foreach (var node in nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.KillServerAsync(node, ColdScenario, token), failures).ConfigureAwait(false);
        }
        foreach (var node in nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.BeginProcessRestartAsync(node, token), failures).ConfigureAwait(false);
        }
        foreach (var node in nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.RestartServerAsync(node, token), failures).ConfigureAwait(false);
        }
        foreach (var node in nodes)
        {
            await ServerFailureObserver.ObserveAsync(async () => await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(
                node, WaitBehavior.WaitOnResourceUnavailable, token), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
