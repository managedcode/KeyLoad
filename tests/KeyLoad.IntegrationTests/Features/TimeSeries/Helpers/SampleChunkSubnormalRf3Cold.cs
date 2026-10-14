using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkSubnormalRf3Cold
{
    private static readonly string[] Nodes =
        [TimeSeriesRf3Scenario.Node1, TimeSeriesRf3Scenario.Node2, TimeSeriesRf3Scenario.Node3];

    internal static async Task RunAsync(ClusterFixture fixture, SampleChunkSubnormalRf3State original,
        CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var node in Nodes)
        {
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() =>
                fixture.KillContainerAsync(node, SampleChunkRf3Protocol.ColdScenario, token), failures).ConfigureAwait(false);
        }
        foreach (var node in Nodes)
        {
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() =>
                fixture.BeginContainerRestartAsync(node, token), failures).ConfigureAwait(false);
        }
        foreach (var node in Nodes)
        {
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() =>
                fixture.RestartContainerAsync(node, token), failures).ConfigureAwait(false);
        }
        foreach (var node in Nodes)
        {
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(node,
                    WaitBehavior.WaitOnResourceUnavailable, token).ConfigureAwait(false), failures);
        }
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        await SampleChunkSubnormalRf3Callers.RunOwnedAsync(fixture, async (sdk, mcp) =>
        {
            await SampleChunkSubnormalRf3Replay.RunAsync(original, sdk, mcp, original.CorrectionReceipt, false, token);
            return original;
        }, token);
    }
}
