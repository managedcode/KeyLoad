using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.StorageReplication;

namespace KeyLoad.IntegrationTests.TimeSeries;

internal static class SampleChunkRetentionClusterCold
{
    private static readonly string[] Nodes = [TimeSeriesClusterScenario.Node1, TimeSeriesClusterScenario.Node2, TimeSeriesClusterScenario.Node3];

    internal static async Task RunAsync(ClusterFixture fixture, SampleChunkRetentionClusterContinuation original,
        CancellationToken token)
    {
        foreach (var node in Nodes)
        {
            await fixture.KillServerAsync(node, SampleChunkRetentionClusterProtocol.ColdScenario, token).ConfigureAwait(false);
        }
        foreach (var node in Nodes)
        {
            await fixture.BeginProcessRestartAsync(node, token).ConfigureAwait(false);
        }
        foreach (var node in Nodes)
        {
            await fixture.RestartServerAsync(node, token).ConfigureAwait(false);
        }
        foreach (var node in Nodes)
        {
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(node, WaitBehavior.WaitOnResourceUnavailable, token).ConfigureAwait(false);
        }
        await global::KeyLoad.IntegrationTests.OrleansExecution.RequestCqrsClusterCallers.RunOwnedAsync(
            fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async callers =>
            {
                var sdk = callers.Sdk;
                await SampleChunkRetentionClusterHealthy.RequireAsync(original, sdk, callers.Mcp, token);
                await SampleChunkRetainedIdentityClusterFlow.HealthyAsync(original, sdk, callers.Mcp, token);
                return true;
            }, token);
    }
}
