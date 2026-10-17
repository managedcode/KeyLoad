using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class CapturedPolicyRf3Replay
{
    internal static async Task RequireAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, FeedLiveRf3Scenario scenario, CommandRequest original,
        CommitReceipt receipt, CancellationToken token)
    {
        await CapturedPolicyRf3Current.BracketAsync(administrator, reader, scenario, true, async () =>
            await SqlRf3Protocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(
                await administrator.Sdk.CommitAsync(original, token))), token);
        await CapturedPolicyRf3Current.BracketAsync(administrator, reader, scenario, true, async () =>
            await SqlRf3Protocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, original, token))).Value), token);
    }
}
