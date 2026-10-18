using KeyLoad.IntegrationTests.Features.ClusterRouting;
using P = KeyLoad.IntegrationTests.Features.ClientApi.McpAnalyticsDropDiscoveryProtocol;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

internal static class McpAnalyticsDropDiscoveryFlow
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async caller =>
            {
                foreach (var name in new[] { AnalyticsStoreProtocol.ReadTool, AnalyticsStoreProtocol.AggregateTool, P.DropTool })
                {
                    await McpDiscoveryAssertions.VerifyAsync(await caller.Mcp.Client.DiscoverKeyLoadToolAsync(name, token));
                }

                var state = await McpAnalyticsDropDiscoverySeed.CreateAsync(caller, token);
                await McpAnalyticsDropDiscoveryAssertions.ReadsAsync(caller, state, token);
                await McpAnalyticsDropDiscoveryAuthorization.VerifyAsync(fixture, caller, state, token);
                await McpAnalyticsDropDiscoveryRetirement.VerifyAsync(caller, state, token);
                return true;
            }, token);
    }
}
