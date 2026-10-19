using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.StorageReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class TextProtectedPublicColdRestart
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        string[] nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];
        var failures = new List<Exception>();
        foreach (var node in nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.KillServerAsync(node, "execution-text-protected-public-cold", token), failures);
        }
        foreach (var node in nodes)
        {
            await ServerFailureObserver.ObserveAsync(async () => await fixture.BeginProcessRestartAsync(node, token), failures);
        }
        foreach (var node in nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.RestartServerAsync(node, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
