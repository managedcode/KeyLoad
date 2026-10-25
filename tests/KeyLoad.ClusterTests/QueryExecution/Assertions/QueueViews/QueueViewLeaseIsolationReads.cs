using System.Text.Json;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using static KeyLoad.ClusterTests.QueryExecution.QueueViewLeaseIsolationProtocol;

namespace KeyLoad.ClusterTests.QueryExecution;

internal static class QueueViewLeaseIsolationReads
{
    internal static async Task RunAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        QueueViewLeaseIsolationState state, CancellationToken token)
    {
        foreach (var hidden in new[] { false, true })
        {
            for (var route = Empty; route < Routes; route++)
            {
                await QueueViewLeaseIsolationImages.ObserveAsync(root, state, async () =>
                    await QueueViewLeaseIsolationRows.AssertAsync(await ReadAsync(reader, state, route, hidden, token), state, hidden), token);
            }
        }
        await ExplainAsync(root, reader, state, token);
    }

    private static Task ExplainAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        QueueViewLeaseIsolationState state, CancellationToken token)
        => QueueViewLeaseIsolationImages.ObserveAsync(root, state, async () =>
        {
            var original = state.Scenario.QueueAst();
            var explain = original with
            {
                Query = original.Query with
                {
                    Explain = true
                }
            };
            var json = JsonSerializer.Serialize(new
            {
                AccessPath = QueuePath,
                ModelViews = true
            }, JsonDefaults.Options);
            var expected = new QueryRow(ExplainId, Empty, json);
            var sdk = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAstAsync(explain, token));
            var mcp = (await McpCallerAssertions.SuccessAsync<QueryPage>(
                await reader.Mcp.CallAsync(McpCallerTools.QueryAst, explain, token))).Value;
            foreach (var page in new[] { sdk, mcp })
            {
                await Assert.That(page.CutPosition).IsGreaterThan((long)Empty);
                await Assert.That(page.AccessPath).IsEqualTo(QueuePath);
                await Assert.That(page.Cursor).IsNull();
                await SqlClusterProtocol.EqualAsync(new[] { expected }, page.Rows.ToArray());
            }
        }, token);

    internal static async Task<QueryPage> ReadAsync(RequestCqrsClusterCallers caller, QueueViewLeaseIsolationState state,
        int route, bool hidden, CancellationToken token)
    {
        var sql = QueueViewLeaseIsolationRequests.Sql(state, hidden);
        var ast = QueueViewLeaseIsolationRequests.Ast(state, hidden);
        return route switch
        {
            SdkSql => await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryAsync(sql, token)),
            SdkAst => await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryAstAsync(ast, token)),
            McpSql => (await McpCallerAssertions.SuccessAsync<QueryPage>(await caller.Mcp.CallAsync(McpCallerTools.QueryExecute, sql, token))).Value,
            McpAst => (await McpCallerAssertions.SuccessAsync<QueryPage>(await caller.Mcp.CallAsync(McpCallerTools.QueryAst, ast, token))).Value,
            SdkQ1 => await SqlClusterProtocol.SdkAsync<QueryPage>(caller.Sdk, new(state.Scenario.Partition, sql.Sql, AllowFullScan: true), token),
            McpQ1 => await SqlClusterProtocol.McpAsync<QueryPage>(caller.Mcp, new(state.Scenario.Partition, sql.Sql, AllowFullScan: true), token),
            _ => throw new InvalidOperationException(UnknownRoute)
        };
    }
}
