using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkRetentionRf3Cold
{
    private static readonly string[] Nodes = [TimeSeriesRf3Scenario.Node1, TimeSeriesRf3Scenario.Node2, TimeSeriesRf3Scenario.Node3];

    internal static async Task RunAsync(ClusterFixture fixture, SampleChunkRetentionRf3Continuation original,
        CancellationToken token)
    {
        foreach (var node in Nodes)
        { await fixture.KillContainerAsync(node, SampleChunkRetentionRf3Protocol.ColdScenario, token).ConfigureAwait(false); }
        foreach (var node in Nodes)
        { await fixture.RestartContainerAsync(node, token).ConfigureAwait(false); }
        foreach (var node in Nodes)
        { await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(node, WaitBehavior.WaitOnResourceUnavailable, token).ConfigureAwait(false); }
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node1,
            fixture.AdminKey, token).ConfigureAwait(false);
        await SampleChunkRetentionRf3Healthy.RequireAsync(original, sdk, mcp, token);
    }
}
