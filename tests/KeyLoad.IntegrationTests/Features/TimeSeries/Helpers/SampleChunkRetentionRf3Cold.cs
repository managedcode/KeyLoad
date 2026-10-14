using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkRetentionRf3Cold
{
    private static readonly string[] Nodes = [TimeSeriesRf3Scenario.Node1, TimeSeriesRf3Scenario.Node2, TimeSeriesRf3Scenario.Node3];

    internal static async Task RunAsync(ClusterFixture fixture, SampleChunkRetentionRf3Continuation original,
        CancellationToken token)
    {
        foreach (var node in Nodes)
        {
            await fixture.KillContainerAsync(node, SampleChunkRetentionRf3Protocol.ColdScenario, token).ConfigureAwait(false);
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
        await global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers.RunOwnedAsync(
            fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async callers =>
            {
                var sdk = callers.Sdk;
                await SampleChunkRetentionRf3Healthy.RequireAsync(original, sdk, callers.Mcp, token);
                await SampleChunkRetainedIdentityRf3Flow.HealthyAsync(original, sdk, callers.Mcp, token);
                return true;
            }, token);
    }
}
