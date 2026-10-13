using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class TimeSeriesCurrentCapabilityRf3Restart
{
    private const string Scenario = "series-current-capability-public-cold";
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        string[] nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];
        var failures = new List<Exception>();
        foreach (var node in nodes)
        { await ServerFailureObserver.ObserveAsync(() => fixture.KillContainerAsync(node, Scenario, token), failures); }
        foreach (var node in nodes)
        { await ServerFailureObserver.ObserveAsync(async () => await fixture.BeginContainerRestartAsync(node, token), failures); }
        foreach (var node in nodes)
        { await ServerFailureObserver.ObserveAsync(() => fixture.RestartContainerAsync(node, token), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
