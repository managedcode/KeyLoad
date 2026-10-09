using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkRf3Cold
{
    private static readonly string[] Nodes =
        [TimeSeriesRf3Scenario.Node1, TimeSeriesRf3Scenario.Node2, TimeSeriesRf3Scenario.Node3];

    internal static async Task RunAsync(ClusterFixture fixture, SampleChunkRf3Continuation original,
        CancellationToken token)
    {
        // Each kill/restart remains in the actual Aspire-owned runtime; all original volumes stay owned by this fixture.
        foreach (var node in Nodes)
        { await fixture.KillContainerAsync(node, SampleChunkRf3Protocol.ColdScenario, token).ConfigureAwait(false); }
        foreach (var node in Nodes)
        { await fixture.RestartContainerAsync(node, token).ConfigureAwait(false); }
        foreach (var node in Nodes)
        {
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(node,
                WaitBehavior.WaitOnResourceUnavailable, token).ConfigureAwait(false);
        }
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node1,
            fixture.AdminKey, token).ConfigureAwait(false);
        await SampleChunkRf3Assertions.AllReadRoutesAsync(original.Scenario, sdk, mcp, original.Correction, token);
        await SampleChunkRf3Assertions.OriginalReceiptAsync(sdk, mcp, original.OriginalSeal, original.OriginalReceipt, token);
        await SampleChunkRf3Assertions.AllReadRoutesAsync(original.Scenario, sdk, mcp, original.Correction, token);
        await SampleChunkRf3Privacy.ExecuteAsync(fixture, original.Scenario, sdk, original.Correction, token);
    }
}
