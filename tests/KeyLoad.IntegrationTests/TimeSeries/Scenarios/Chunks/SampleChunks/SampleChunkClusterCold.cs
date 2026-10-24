using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.StorageReplication;

namespace KeyLoad.IntegrationTests.TimeSeries;

internal static class SampleChunkClusterCold
{
    private static readonly string[] Nodes =
        [TimeSeriesClusterScenario.Node1, TimeSeriesClusterScenario.Node2, TimeSeriesClusterScenario.Node3];

    internal static async Task RunAsync(ClusterFixture fixture, SampleChunkClusterContinuation original,
        CancellationToken token)
    {
        // Each kill/restart remains in the actual Aspire-owned runtime; all original volumes stay owned by this fixture.
        foreach (var node in Nodes)
        {
            await fixture.KillServerAsync(node, SampleChunkClusterProtocol.ColdScenario, token).ConfigureAwait(false);
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
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(node,
                WaitBehavior.WaitOnResourceUnavailable, token).ConfigureAwait(false);
        }
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node1,
            fixture.AdminKey, token).ConfigureAwait(false);
        await SampleChunkClusterAssertions.AllReadRoutesAsync(original.Scenario, sdk, mcp, original.Correction, token);
        await SampleChunkClusterAssertions.OriginalReceiptAsync(sdk, mcp, original.OriginalSeal, original.OriginalReceipt, token);
        await SampleChunkClusterAssertions.AllReadRoutesAsync(original.Scenario, sdk, mcp, original.Correction, token);
        await SampleChunkClusterPrivacy.ExecuteAsync(fixture, original.Scenario, sdk, original.Correction, token);
    }
}
