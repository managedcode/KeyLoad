using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class CapturedPolicyRf3Refusals
{
    internal static async Task RequireAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, FeedLiveRf3Scenario scenario, ReadChangeFeedRequest feed,
        ReadLiveQueryRequest live, ErrorCode code, bool updated, CancellationToken token)
    {
        await CapturedPolicyRf3Current.BracketAsync(administrator, reader, scenario, updated, async () =>
        {
            var result = await reader.Sdk.ReadChangesAsync(feed, token);
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Problem!.ErrorCode).IsEqualTo(code.ToString());
            await Assert.That(result.Value).IsNull();
        }, token);
        await CapturedPolicyRf3Current.BracketAsync(administrator, reader, scenario, updated, async () =>
            await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(McpCallerTools.ChangesRead,
                feed, token), code, dispatched: true), token);
        await CapturedPolicyRf3Current.BracketAsync(administrator, reader, scenario, updated, async () =>
        {
            var result = await reader.Sdk.ReadLiveQueryAsync(live, token);
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Problem!.ErrorCode).IsEqualTo(code.ToString());
            await Assert.That(result.Value).IsNull();
        }, token);
        await CapturedPolicyRf3Current.BracketAsync(administrator, reader, scenario, updated, async () =>
            await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(McpCallerTools.QueryLiveRead,
                live, token), code, dispatched: true), token);
    }

    internal static async Task StartAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, FeedLiveRf3Scenario scenario, CancellationToken token)
    {
        var request = new StartLiveQueryRequest(scenario.Query);
        await CapturedPolicyRf3Current.BracketAsync(administrator, reader, scenario, false, async () =>
        {
            var result = await reader.Sdk.StartLiveQueryAsync(request, token);
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Problem!.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
            await Assert.That(result.Value).IsNull();
        }, token);
        await CapturedPolicyRf3Current.BracketAsync(administrator, reader, scenario, false, async () =>
            await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(McpCallerTools.QueryLiveStart,
                request, token), ErrorCode.PermissionDenied, dispatched: true), token);
    }
}
