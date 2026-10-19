using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.StorageReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class FeedLiveClusterCold
{
    private static readonly string[] Nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];

    internal static async Task RestartAsync(ClusterFixture fixture, CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var node in Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.KillServerAsync(node,
            FeedLiveClusterProtocol.ColdScenario, token), failures);
        }
        foreach (var node in Nodes)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
                await fixture.BeginProcessRestartAsync(node, token), failures);
        }
        foreach (var node in Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.RestartServerAsync(node, token), failures);
        }
        foreach (var node in Nodes)
        {
            await ServerFailureObserver.ObserveAsync(async () => await fixture.App.ResourceNotifications
            .WaitForResourceHealthyAsync(node, WaitBehavior.WaitOnResourceUnavailable, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
