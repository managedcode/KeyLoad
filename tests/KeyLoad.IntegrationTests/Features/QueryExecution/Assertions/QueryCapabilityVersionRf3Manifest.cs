using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueryCapabilityVersionRf3Manifest
{
    internal static async Task RunAsync(RequestCqrsRf3Callers caller, PartitionRef partition, CancellationToken token)
    {
        var expected = QueryCapabilityVersionRf3Expected.Manifest(QueryCapabilityVersionRf3Requests.Limits());
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryCapabilitiesAsync(token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<QueryCapabilityManifest>(
            await caller.Mcp.CallAsync(McpCallerTools.QueryCapabilities, new
            {
            }, token))).Value);
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.SdkAsync<QueryCapabilityManifest>(caller.Sdk,
            SqlRf3Protocol.NoBodyCall(partition, McpCallerTools.QueryCapabilities), token));
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.McpAsync<QueryCapabilityManifest>(caller.Mcp,
            SqlRf3Protocol.NoBodyCall(partition, McpCallerTools.QueryCapabilities), token));
    }
}
