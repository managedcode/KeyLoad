using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.StorageReplication;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.TimeSeries;

internal static class TimeSeriesAggregateScanBoundClusterRestart
{
    private const string Scenario = "series-aggregate-scan-bound-public-cold";
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        string[] nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];
        var failures = new List<Exception>();
        foreach (var node in nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.KillServerAsync(node, Scenario, token), failures);
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
