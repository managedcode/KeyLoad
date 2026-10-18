using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkRetentionClusterCold
{
    private static readonly string[] Nodes = [TimeSeriesClusterScenario.Node1, TimeSeriesClusterScenario.Node2, TimeSeriesClusterScenario.Node3];

    internal static async Task RunAsync(ClusterFixture fixture, SampleChunkRetentionClusterContinuation original,
        CancellationToken token)
    {
        foreach (var node in Nodes)
        {
            await fixture.KillContainerAsync(node, SampleChunkRetentionClusterProtocol.ColdScenario, token).ConfigureAwait(false);
        }
        foreach (var node in Nodes)
        {
            await fixture.BeginContainerRestartAsync(node, token).ConfigureAwait(false);
        }
        foreach (var node in Nodes)
        {
            await fixture.RestartContainerAsync(node, token).ConfigureAwait(false);
        }
        foreach (var node in Nodes)
        {
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(node, WaitBehavior.WaitOnResourceUnavailable, token).ConfigureAwait(false);
        }
        await global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsClusterCallers.RunOwnedAsync(
            fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async callers =>
            {
                var sdk = callers.Sdk;
                await SampleChunkRetentionClusterHealthy.RequireAsync(original, sdk, callers.Mcp, token);
                await SampleChunkRetainedIdentityClusterFlow.HealthyAsync(original, sdk, callers.Mcp, token);
                return true;
            }, token);
    }
}
