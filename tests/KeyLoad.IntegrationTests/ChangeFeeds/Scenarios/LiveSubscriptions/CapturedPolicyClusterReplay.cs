using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class CapturedPolicyClusterReplay
{
    internal static async Task RequireAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, FeedLiveClusterScenario scenario, CommandRequest original,
        CommitReceipt receipt, CancellationToken token)
    {
        await CapturedPolicyClusterCurrent.BracketAsync(administrator, reader, scenario, true, async () =>
            await SqlClusterProtocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(
                await administrator.Sdk.CommitAsync(original, token))), token);
        await CapturedPolicyClusterCurrent.BracketAsync(administrator, reader, scenario, true, async () =>
            await SqlClusterProtocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, original, token))).Value), token);
    }
}
