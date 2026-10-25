using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.ChangeFeeds;

internal static class CapturedPolicyClusterRefusals
{
    internal static async Task RequireAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, FeedLiveClusterScenario scenario, ReadChangeFeedRequest feed,
        ReadLiveQueryRequest live, ErrorCode code, bool updated, CancellationToken token)
    {
        await CapturedPolicyClusterCurrent.BracketAsync(administrator, reader, scenario, updated, async () =>
        {
            var result = await reader.Sdk.ReadChangesAsync(feed, token);
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Problem!.ErrorCode).IsEqualTo(code.ToString());
            await Assert.That(result.Value).IsNull();
        }, token);
        await CapturedPolicyClusterCurrent.BracketAsync(administrator, reader, scenario, updated, async () =>
        {
            _ = (await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(McpCallerTools.ChangesRead,
                feed, token), code, dispatched: true)).Value;
        }, token);
        await CapturedPolicyClusterCurrent.BracketAsync(administrator, reader, scenario, updated, async () =>
        {
            var result = await reader.Sdk.ReadLiveQueryAsync(live, token);
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Problem!.ErrorCode).IsEqualTo(code.ToString());
            await Assert.That(result.Value).IsNull();
        }, token);
        await CapturedPolicyClusterCurrent.BracketAsync(administrator, reader, scenario, updated, async () =>
        {
            _ = (await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(McpCallerTools.QueryLiveRead,
                live, token), code, dispatched: true)).Value;
        }, token);
    }

    internal static async Task StartAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, FeedLiveClusterScenario scenario, CancellationToken token)
    {
        var request = new StartLiveQueryRequest(scenario.Query);
        await CapturedPolicyClusterCurrent.BracketAsync(administrator, reader, scenario, false, async () =>
        {
            var result = await reader.Sdk.StartLiveQueryAsync(request, token);
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Problem!.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
            await Assert.That(result.Value).IsNull();
        }, token);
        await CapturedPolicyClusterCurrent.BracketAsync(administrator, reader, scenario, false, async () =>
        {
            _ = (await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(McpCallerTools.QueryLiveStart,
                request, token), ErrorCode.PermissionDenied, dispatched: true)).Value;
        }, token);
    }
}
