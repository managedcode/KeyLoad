using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class FeedLiveRf3Cold
{
    private static readonly string[] Nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];

    internal static async Task RestartAsync(ClusterFixture fixture, CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var node in Nodes)
        { await ServerFailureObserver.ObserveAsync(() => fixture.KillContainerAsync(node,
            FeedLiveRf3Protocol.ColdScenario, token), failures); }
        foreach (var node in Nodes)
        { await ServerFailureObserver.ObserveAsync(() => fixture.RestartContainerAsync(node, token), failures); }
        foreach (var node in Nodes)
        { await ServerFailureObserver.ObserveAsync(async () => await fixture.App.ResourceNotifications
            .WaitForResourceHealthyAsync(node, WaitBehavior.WaitOnResourceUnavailable, token), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
