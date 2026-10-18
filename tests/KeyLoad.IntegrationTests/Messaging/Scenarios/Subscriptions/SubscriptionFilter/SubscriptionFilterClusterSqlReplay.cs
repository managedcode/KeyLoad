using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class SubscriptionFilterClusterSqlReplay
{
    private const string Parameter = "args";
    private const string ConfigureCall = "CALL keyload_subscriptions_configure(@args)";

    internal static async Task RunAsync(KeyLoadClient manager, McpOfficialClient mcp,
        SubscriptionFilterClusterUpdated updated, CancellationToken token)
    {
        var request = new SqlOperationRequest(updated.Request.Subscription.Source.Partition, ConfigureCall,
            new(StringComparer.Ordinal)
            {
                [Parameter] = JsonSerializer.SerializeToElement(
                McpOfficialClient.Arguments(updated.Request), JsonDefaults.Options)
            });
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await manager.ExecuteSqlAsync(request, token));
        await SubscriptionFilterClusterAssertions.EqualAsync(sdk.Deserialize<SubscriptionInfo>(JsonDefaults.Options)!, updated.Result);
        var official = (await McpCallerAssertions.SuccessAsync<JsonElement>(await mcp.CallAsync(SqlOperationProtocol.ToolName, request, token))).Value;
        await SubscriptionFilterClusterAssertions.EqualAsync(official.Deserialize<SubscriptionInfo>(JsonDefaults.Options)!, updated.Result);
    }
}
