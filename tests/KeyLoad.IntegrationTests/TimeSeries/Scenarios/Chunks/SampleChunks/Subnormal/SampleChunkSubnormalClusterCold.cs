using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.StorageReplication;

namespace KeyLoad.IntegrationTests.TimeSeries;

internal static class SampleChunkSubnormalClusterCold
{
    private static readonly string[] Nodes =
        [TimeSeriesClusterScenario.Node1, TimeSeriesClusterScenario.Node2, TimeSeriesClusterScenario.Node3];

    internal static async Task RunAsync(ClusterFixture fixture, SampleChunkSubnormalClusterState original,
        CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var node in Nodes)
        {
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() =>
                fixture.KillServerAsync(node, SampleChunkClusterProtocol.ColdScenario, token), failures).ConfigureAwait(false);
        }
        foreach (var node in Nodes)
        {
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() =>
                fixture.BeginProcessRestartAsync(node, token), failures).ConfigureAwait(false);
        }
        foreach (var node in Nodes)
        {
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() =>
                fixture.RestartServerAsync(node, token), failures).ConfigureAwait(false);
        }
        foreach (var node in Nodes)
        {
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(node,
                    WaitBehavior.WaitOnResourceUnavailable, token).ConfigureAwait(false), failures);
        }
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        await SampleChunkSubnormalClusterCallers.RunOwnedAsync(fixture, async (sdk, mcp) =>
        {
            await SampleChunkSubnormalClusterReplay.RunAsync(original, sdk, mcp, original.CorrectionReceipt, false, token);
            return original;
        }, token);
    }
}
