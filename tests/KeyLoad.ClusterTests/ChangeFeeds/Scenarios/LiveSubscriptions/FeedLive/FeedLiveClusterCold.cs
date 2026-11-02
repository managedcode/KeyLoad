using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.StorageReplication;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.ChangeFeeds;

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
        ServerFailureObserver.ThrowIfAny(failures);
        foreach (var node in Nodes)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
                await fixture.BeginProcessRestartAsync(node, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        foreach (var node in Nodes)
        {
            await fixture.RestartServerAsync(node, token);
        }
        await ClusterMembershipReadiness.WaitForActiveSilosAsync(fixture, Nodes, token);
    }
}
