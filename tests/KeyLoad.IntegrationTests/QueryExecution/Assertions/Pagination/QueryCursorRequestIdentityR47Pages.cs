using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class QueryCursorRequestIdentityR47Pages
{
    internal static async Task<QueryPage[]> FreshAsync(RequestCqrsClusterCallers root,
        RequestCqrsClusterCallers reader, QueryCursorRequestIdentityR47State state, CancellationToken token)
    {
        var pages = new QueryPage[2];
        for (var route = 0; route < pages.Length; route++)
        {
            var first = await ReadAsync(reader, state.OriginalRequest, route, token);
            await RowsAsync(first, [state.CompleteRows[1]], state.AccessPath);
            await Assert.That(string.IsNullOrEmpty(first.Cursor)).IsFalse();
            pages[route] = first;
            await QueryCursorRequestIdentityR47Current.RequireAsync(root, reader, state, token);
            await ContinueAsync(root, reader, state, first, 1 - route, token);
        }
        return pages;
    }

    internal static async Task ContinueAsync(RequestCqrsClusterCallers root,
        RequestCqrsClusterCallers reader, QueryCursorRequestIdentityR47State state,
        QueryPage original, int route, CancellationToken token)
    {
        var cursor = original.Cursor;
        for (var index = 2; index < state.CompleteRows.Length; index++)
        {
            var page = await ReadAsync(reader, state.OriginalRequest with
            {
                Cursor = cursor
            }, route, token);
            await RowsAsync(page, [state.CompleteRows[index]], state.AccessPath);
            await Assert.That(page.CutPosition).IsEqualTo(original.CutPosition);
            if (index == state.CompleteRows.Length - 1)
            {
                await Assert.That(page.Cursor).IsNull();
            }
            else
            {
                await Assert.That(string.IsNullOrEmpty(page.Cursor)).IsFalse();
            }
            await QueryCursorRequestIdentityR47Current.RequireAsync(root, reader, state, token);
            cursor = page.Cursor;
        }
    }

    private static async Task<QueryPage> ReadAsync(RequestCqrsClusterCallers reader,
        QueryRequest request, int route, CancellationToken token)
        => route == 0 ? await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAsync(request, token))
            : (await McpCallerAssertions.SuccessAsync<QueryPage>(await reader.Mcp.CallAsync(
                McpCallerTools.QueryExecute, request, token))).Value;

    private static async Task RowsAsync(QueryPage page, QueryRow[] expected, string accessPath)
    {
        await SqlClusterProtocol.EqualAsync(expected, page.Rows.ToArray());
        await Assert.That(page.AccessPath).IsEqualTo(accessPath);
        await Assert.That(page.CutPosition).IsGreaterThan(0L);
    }
}
