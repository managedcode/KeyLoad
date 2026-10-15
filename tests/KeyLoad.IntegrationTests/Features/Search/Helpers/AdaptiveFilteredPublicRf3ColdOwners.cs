using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class AdaptiveFilteredPublicRf3ColdOwners
{
    private const string Scenario = "adaptive-filter-public-cold";
    private static readonly string[] Nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var stopped = new List<string>();
        var failures = new List<Exception>();
        foreach (var node in Nodes)
        {
            var prior = failures.Count;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.KillContainerAsync(node, Scenario, token);
                stopped.Add(node);
            }, failures);
            if (failures.Count != prior)
            {
                break;
            }
        }
        foreach (var node in stopped)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.BeginContainerRestartAsync(node, token), failures);
        }
        foreach (var node in stopped)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.RestartContainerAsync(node, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
