using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class ConnectionQueryContextColdOwners
{
    internal static async Task RunAsync(ContainerRuntimeControl runtime, string scenario, CancellationToken token)
    {
        var failures = new List<Exception>();
        var stopped = new List<string>();
        foreach (var node in RequestCqrsProbeFixtureProtocol.Nodes)
        {
            var before = failures.Count;
            await ServerFailureObserver.ObserveAsync(() => runtime.KillAsync(node, scenario, token), failures);
            if (failures.Count != before)
            {
                break;
            }
            stopped.Add(node);
        }
        foreach (var node in stopped)
        {
            await ServerFailureObserver.ObserveAsync(() => runtime.BeginRestartAsync(node, token), failures);
        }
        foreach (var node in stopped)
        {
            await ServerFailureObserver.ObserveAsync(() => runtime.RestartAsync(node, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
