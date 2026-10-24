using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class QueryCapabilityVersionClusterManifest
{
    internal static async Task RunAsync(RequestCqrsClusterCallers caller, PartitionRef partition, CancellationToken token)
    {
        var expected = QueryCapabilityVersionClusterExpected.Manifest(QueryCapabilityVersionClusterRequests.Limits());
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryCapabilitiesAsync(token)));
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<QueryCapabilityManifest>(
            await caller.Mcp.CallWithoutBodyAsync(McpCallerTools.QueryCapabilities, token))).Value);
        await SqlClusterProtocol.EqualAsync(expected, await SqlClusterProtocol.SdkAsync<QueryCapabilityManifest>(caller.Sdk,
            SqlClusterProtocol.NoBodyCall(partition, McpCallerTools.QueryCapabilities), token));
        await SqlClusterProtocol.EqualAsync(expected, await SqlClusterProtocol.McpAsync<QueryCapabilityManifest>(caller.Mcp,
            SqlClusterProtocol.NoBodyCall(partition, McpCallerTools.QueryCapabilities), token));
    }
}
