using System.Text.Json;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class QueryCapabilityVersionClusterPages
{
    internal static async Task RunAsync(RequestCqrsClusterCallers reader, QueryCapabilityVersionClusterState state, CancellationToken token)
    {
        await QueryCapabilityVersionClusterManifest.RunAsync(reader, state.Partition, token);
        foreach (var version in new[] { 1 })
        {
            var builder = QueryCapabilityVersionClusterRequests.Builder(state.Partition) with
            {
                AstVersion = version
            };
            await SqlClusterProtocol.EqualAsync(QueryCapabilityVersionClusterRequests.Ast(state.Partition, version), builder);
            var request = JsonSerializer.Deserialize<AstQueryRequest>(JsonSerializer.Serialize(builder, JsonDefaults.Options), JsonDefaults.Options)!;
            await SqlClusterProtocol.EqualAsync(builder, request);
            for (var route = 0; route < 6; route++)
            {
                await RowsAsync(reader, state, request, version, route, token);
            }
            await ExplainAsync(reader, state, request, token);
        }
    }
    private static async Task RowsAsync(RequestCqrsClusterCallers reader, QueryCapabilityVersionClusterState state,
        AstQueryRequest request, int version, int route, CancellationToken token)
    {
        var sql = QueryCapabilityVersionClusterRequests.Sql(state.Partition, version);
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
            2 => await SqlClusterProtocol.SdkAsync<QueryPage>(reader.Sdk, new(sql.Partition, sql.Sql, sql.Parameters, sql.AllowFullScan, sql.Cursor, QueryDialectVersion: sql.QueryDialectVersion), token),
            3 => await SqlClusterProtocol.McpAsync<QueryPage>(reader.Mcp, new(sql.Partition, sql.Sql, sql.Parameters, sql.AllowFullScan, sql.Cursor, QueryDialectVersion: sql.QueryDialectVersion), token),
            4 => await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAstAsync(ast, token)),
            5 => (await McpCallerAssertions.SuccessAsync<QueryPage>(await reader.Mcp.CallAsync(McpCallerTools.QueryAst, ast, token))).Value,
            _ => throw new InvalidOperationException("The query example route is invalid.")
        };
    private static async Task ExplainAsync(RequestCqrsClusterCallers reader, QueryCapabilityVersionClusterState state, AstQueryRequest request, CancellationToken token)
    {
        var plan = QueryCapabilityVersionClusterRequests.Explain(state.Partition) with
        {
            AstVersion = request.AstVersion
        };
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
        var body = JsonSerializer.Serialize(new
        {
            accessPath = "bounded-full-scan",
            atomicPartition = state.Partition.AtomicPartitionId,
            scanBudget = 10000
        }, JsonDefaults.Options);
        foreach (var page in pages)
        {
            await Assert.That(page.CutPosition).IsGreaterThan(0L);
            await SqlClusterProtocol.EqualAsync(new QueryPage([new("explain", 0L, body)], null, page.CutPosition, "bounded-full-scan"), page);
        }
    }
    private static readonly (string Id, string Json, bool Final)[] Rows =
        [("b", """{"title":"Bravo","score":20}""", false), ("d", """{"title":"Delta","score":40}""", true)];
}
