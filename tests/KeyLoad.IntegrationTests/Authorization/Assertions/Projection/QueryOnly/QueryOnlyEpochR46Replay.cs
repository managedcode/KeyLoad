using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class QueryOnlyEpochR46Replay
{
    internal static async Task RequireAsync(RequestCqrsClusterCallers root,
        RequestCqrsClusterCallers reader, QueryOnlyEpochR46State state, CancellationToken token)
    {
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(state.Original.Seed, token));
        await SqlClusterProtocol.EqualAsync(state.Original.Receipt, receipt);
        await QueryOnlyEpochR46Current.RequireAsync(root, reader, state, token);
        var reply = await root.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Original.Seed, token);
        await SqlClusterProtocol.EqualAsync(state.Original.Receipt,
            (await McpCallerAssertions.SuccessAsync<CommitReceipt>(reply)).Value);
        await QueryOnlyEpochR46Current.RequireAsync(root, reader, state, token);
    }
}
