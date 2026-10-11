using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeAnnPublicColdOwners
{
    private const string Scenario = "native-ann-public-two-cold";
    private static readonly string[] Nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];

    internal static async Task RestartAsync(ClusterFixture fixture, CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var node in Nodes)
        { await ServerFailureObserver.ObserveAsync(() => fixture.KillContainerAsync(node, Scenario, token), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        foreach (var node in Nodes)
        { await ServerFailureObserver.ObserveAsync(() => fixture.BeginContainerRestartAsync(node, token), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        foreach (var node in Nodes)
        { await ServerFailureObserver.ObserveAsync(() => fixture.RestartContainerAsync(node, token), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
