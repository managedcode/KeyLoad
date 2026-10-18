using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueryCapabilityVersionClusterManifest
{
    internal static async Task RunAsync(RequestCqrsClusterCallers caller, PartitionRef partition, CancellationToken token)
    {
        var expected = QueryCapabilityVersionClusterExpected.Manifest(QueryCapabilityVersionClusterRequests.Limits());
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryCapabilitiesAsync(token)));
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<QueryCapabilityManifest>(
            await caller.Mcp.CallAsync(McpCallerTools.QueryCapabilities, new
            {
            }, token))).Value);
        await SqlClusterProtocol.EqualAsync(expected, await SqlClusterProtocol.SdkAsync<QueryCapabilityManifest>(caller.Sdk,
            SqlClusterProtocol.NoBodyCall(partition, McpCallerTools.QueryCapabilities), token));
        await SqlClusterProtocol.EqualAsync(expected, await SqlClusterProtocol.McpAsync<QueryCapabilityManifest>(caller.Mcp,
            SqlClusterProtocol.NoBodyCall(partition, McpCallerTools.QueryCapabilities), token));
    }
}
