using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.StorageReplication;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.QueryExecution;

internal static class QueryCursorRequestIdentityR47Cold
{
    private static readonly string[] Nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];

    internal static async Task RestartAsync(ClusterFixture fixture, CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var node in Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.KillServerAsync(node,
                QueryCursorRequestIdentityR47Protocol.ColdScenario, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        foreach (var node in Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.BeginProcessRestartAsync(node, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        foreach (var node in Nodes)
        {
            await fixture.RestartServerAsync(node, token);
        }
        await ClusterMembershipReadiness.WaitForActiveSilosAsync(fixture, Nodes, token);
    }
}
