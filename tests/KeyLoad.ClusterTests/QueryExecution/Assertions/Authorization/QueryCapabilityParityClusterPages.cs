using System.Text.Json;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.Query;

namespace KeyLoad.ClusterTests.QueryExecution;

internal static class QueryCapabilityParityClusterPages
{
    internal static async Task RunAsync(RequestCqrsClusterCallers reader, QueryCapabilityParityClusterState state, CancellationToken token)
    {
        await QueryCapabilityParityClusterManifest.RunAsync(reader, state.Partition, token);
        var builder = QueryCapabilityParityClusterRequests.Builder(state.Partition);
        await SqlClusterProtocol.EqualAsync(QueryCapabilityParityClusterRequests.Ast(state.Partition), builder);
        var request = JsonSerializer.Deserialize<AstQueryRequest>(JsonSerializer.Serialize(builder, JsonDefaults.Options), JsonDefaults.Options)!;
        await SqlClusterProtocol.EqualAsync(builder, request);
        for (var route = 0; route < 6; route++)
        {
            await RowsAsync(reader, state, request, route, token);
        }
        await ExplainAsync(reader, state, request, token);
    }
    private static async Task RowsAsync(RequestCqrsClusterCallers reader, QueryCapabilityParityClusterState state,
        AstQueryRequest request, int route, CancellationToken token)
    {
        var sql = QueryCapabilityParityClusterRequests.Sql(state.Partition);
        long? cut = null;
        foreach (var (id, body, final) in Rows)
        {
            var page = await ReadAsync(reader, sql, request, route, token);
            cut ??= page.CutPosition;
            await Assert.That(cut.Value).IsGreaterThan(0L);
            await Assert.That(page.Cursor is null).IsEqualTo(final);
            await SqlClusterProtocol.EqualAsync(new QueryPage([new(id, 1L, body, false, [])], page.Cursor, cut.Value, "bounded-full-scan"), page);
            sql = sql with
            {
                Cursor = page.Cursor
            };
            request = request with
            {
                Cursor = page.Cursor
            };
        }
    }
    private static async Task<QueryPage> ReadAsync(RequestCqrsClusterCallers reader, QueryRequest sql, AstQueryRequest ast, int route, CancellationToken token)
        => route switch
        {
            0 => await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAsync(sql, token)),
            1 => (await McpCallerAssertions.SuccessAsync<QueryPage>(await reader.Mcp.CallAsync(McpCallerTools.QueryExecute, sql, token))).Value,
            2 => await SqlClusterProtocol.SdkAsync<QueryPage>(reader.Sdk, new(sql.Partition, sql.Sql, sql.Parameters, sql.AllowFullScan, sql.Cursor), token),
            3 => await SqlClusterProtocol.McpAsync<QueryPage>(reader.Mcp, new(sql.Partition, sql.Sql, sql.Parameters, sql.AllowFullScan, sql.Cursor), token),
            4 => await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAstAsync(ast, token)),
            5 => (await McpCallerAssertions.SuccessAsync<QueryPage>(await reader.Mcp.CallAsync(McpCallerTools.QueryAst, ast, token))).Value,
            _ => throw new InvalidOperationException("The query example route is invalid.")
        };
    private static async Task ExplainAsync(RequestCqrsClusterCallers reader, QueryCapabilityParityClusterState state, AstQueryRequest request, CancellationToken token)
    {
        var plan = QueryCapabilityParityClusterRequests.Explain(state.Partition);
        var expected = request with
        {
            Query = request.Query with
            {
                Explain = true
            }
        };
        await SqlClusterProtocol.EqualAsync(expected, plan);
        var call = SqlClusterProtocol.Call(state.Partition, McpCallerTools.QueryAst, plan);
        var pages = new[]
        {
            await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAstAsync(plan, token)),
            (await McpCallerAssertions.SuccessAsync<QueryPage>(await reader.Mcp.CallAsync(McpCallerTools.QueryAst, plan, token))).Value,
            await SqlClusterProtocol.SdkAsync<QueryPage>(reader.Sdk, call, token),
            await SqlClusterProtocol.McpAsync<QueryPage>(reader.Mcp, call, token)
        };
        foreach (var page in pages)
        {
            await Assert.That(page.CutPosition).IsGreaterThan(0L);
            var body = JsonSerializer.Serialize(new
            {
                accessPath = "bounded-full-scan",
                atomicPartition = state.Partition.AtomicPartitionId,
                ReadCut = page.CutPosition,
                Scan = "bounded-full-scan",
                Index = (string?)null,
                SeekFields = Array.Empty<string>(),
                HasLowerBound = false,
                HasUpperBound = false,
                ResidualFields = new[] { "/active", "/score" },
                Order = new Ordering[] { new("/order_id", false) },
                RetainedRows = 1L,
                Limit = 1,
                Offset = 0,
                Projection = new Selection[] { new("/title", "title"), new("/score", "score") },
                Operators = new[] { "bounded-full-scan", "row-authorization", "residual-filter", "top-n-heap", "projection", "page" }
            }, JsonDefaults.Options);
            await SqlClusterProtocol.EqualAsync(new QueryPage([new("explain", 0L, body)], null, page.CutPosition, "bounded-full-scan"), page);
        }
    }
    private static readonly (string Id, string Json, bool Final)[] Rows =
        [("b", """{"title":"Bravo","score":20}""", false), ("d", """{"title":"Delta","score":40}""", true)];
}
