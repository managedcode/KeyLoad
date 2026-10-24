using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class QueryCursorRequestIdentityR47Current
{
    internal static async Task RequireAsync(RequestCqrsClusterCallers root,
        RequestCqrsClusterCallers reader, QueryCursorRequestIdentityR47State state, CancellationToken token)
    {
        await ImageAsync(await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.QueryAsync(state.CompleteRequest, token)), state);
        await ImageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(await root.Mcp.CallAsync(
            McpCallerTools.QueryExecute, state.CompleteRequest, token))).Value, state);
        await DocumentsAsync(root, state, token);
        await DocumentsAsync(reader, state, token);
        var listed = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ListResourcesAsync(
            new(state.Partition.TenantId, state.Partition.DatabaseId), token));
        foreach (var expected in state.Definitions)
        {
            await SqlClusterProtocol.EqualAsync(expected, listed.Items.Single(item => item.Name == expected.Name));
        }
        await SqlClusterProtocol.EqualAsync(state.Outbox, await OutboxAsync(root, state.Partition, token));
    }

    internal static async Task<OutboxStatus> OutboxAsync(RequestCqrsClusterCallers root,
        PartitionRef partition, CancellationToken token)
    {
        var value = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(partition, token));
        await SqlClusterProtocol.EqualAsync(value, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await root.Mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(partition), token))).Value);
        return value;
    }

    private static async Task ImageAsync(QueryPage page, QueryCursorRequestIdentityR47State state)
    {
        await SqlClusterProtocol.EqualAsync(state.CompleteRows, page.Rows.ToArray());
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo(state.AccessPath);
        await Assert.That(page.CutPosition).IsGreaterThan(0L);
    }

    private static async Task DocumentsAsync(RequestCqrsClusterCallers caller,
        QueryCursorRequestIdentityR47State state, CancellationToken token)
    {
        foreach (var expected in state.Documents)
        {
            await SqlClusterProtocol.EqualAsync(expected, await PublicReadAssertions.DocumentAsync(
                await caller.Sdk.GetAsync(expected.Reference, token)));
            await SqlClusterProtocol.EqualAsync(expected, (await PublicReadAssertions.DocumentAsync(
                await caller.Mcp.CallAsync(McpCallerTools.DocumentsGet,
                    new GetDocumentRequest(expected.Reference), token))).Value);
        }
    }
}
