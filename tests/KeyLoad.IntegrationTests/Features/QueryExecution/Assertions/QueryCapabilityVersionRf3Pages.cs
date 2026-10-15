using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueryCapabilityVersionRf3Pages
{
    internal static async Task RunAsync(RequestCqrsRf3Callers reader, QueryCapabilityVersionRf3State state, CancellationToken token)
    {
        await QueryCapabilityVersionRf3Manifest.RunAsync(reader, state.Partition, token);
        foreach (var version in new[] { 1, 2 })
        {
            var builder = QueryCapabilityVersionRf3Requests.Builder(state.Partition) with
            {
                AstVersion = version
            };
            await SqlRf3Protocol.EqualAsync(QueryCapabilityVersionRf3Requests.Ast(state.Partition, version), builder);
            var request = JsonSerializer.Deserialize<AstQueryRequest>(JsonSerializer.Serialize(builder, JsonDefaults.Options), JsonDefaults.Options)!;
            await SqlRf3Protocol.EqualAsync(builder, request);
            for (var route = 0; route < 6; route++)
            {
                await RowsAsync(reader, state, request, version, route, token);
            }
            await ExplainAsync(reader, state, request, token);
        }
    }
    private static async Task RowsAsync(RequestCqrsRf3Callers reader, QueryCapabilityVersionRf3State state,
        AstQueryRequest request, int version, int route, CancellationToken token)
    {
        var sql = QueryCapabilityVersionRf3Requests.Sql(state.Partition, version);
        long? cut = null;
        foreach (var (id, body, final) in Rows)
        {
            var page = await ReadAsync(reader, sql, request, route, token);
            cut ??= page.CutPosition;
            await Assert.That(cut.Value).IsGreaterThan(0L);
            await Assert.That(page.Cursor is null).IsEqualTo(final);
            await SqlRf3Protocol.EqualAsync(new QueryPage([new(id, 1L, body, false, [])], page.Cursor, cut.Value, "bounded-full-scan"), page);
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
    private static async Task<QueryPage> ReadAsync(RequestCqrsRf3Callers reader, QueryRequest sql, AstQueryRequest ast, int route, CancellationToken token)
        => route switch
        {
            0 => await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAsync(sql, token)),
            1 => (await McpCallerAssertions.SuccessAsync<QueryPage>(await reader.Mcp.CallAsync(McpCallerTools.QueryExecute, sql, token))).Value,
            2 => await SqlRf3Protocol.SdkAsync<QueryPage>(reader.Sdk, new(sql.Partition, sql.Sql, sql.Parameters, sql.AllowFullScan, sql.Cursor, QueryDialectVersion: sql.QueryDialectVersion), token),
            3 => await SqlRf3Protocol.McpAsync<QueryPage>(reader.Mcp, new(sql.Partition, sql.Sql, sql.Parameters, sql.AllowFullScan, sql.Cursor, QueryDialectVersion: sql.QueryDialectVersion), token),
            4 => await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAstAsync(ast, token)),
            5 => (await McpCallerAssertions.SuccessAsync<QueryPage>(await reader.Mcp.CallAsync(McpCallerTools.QueryAst, ast, token))).Value,
            _ => throw new InvalidOperationException("The query example route is invalid.")
        };
    private static async Task ExplainAsync(RequestCqrsRf3Callers reader, QueryCapabilityVersionRf3State state, AstQueryRequest request, CancellationToken token)
    {
        var plan = QueryCapabilityVersionRf3Requests.Explain(state.Partition) with
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
        await SqlRf3Protocol.EqualAsync(expected, plan);
        var call = SqlRf3Protocol.Call(state.Partition, McpCallerTools.QueryAst, plan);
        var pages = new[]
        {
            await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAstAsync(plan, token)),
            (await McpCallerAssertions.SuccessAsync<QueryPage>(await reader.Mcp.CallAsync(McpCallerTools.QueryAst, plan, token))).Value,
            await SqlRf3Protocol.SdkAsync<QueryPage>(reader.Sdk, call, token),
            await SqlRf3Protocol.McpAsync<QueryPage>(reader.Mcp, call, token)
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
            await SqlRf3Protocol.EqualAsync(new QueryPage([new("explain", 0L, body)], null, page.CutPosition, "bounded-full-scan"), page);
        }
    }
    private static readonly (string Id, string Json, bool Final)[] Rows =
        [("b", """{"title":"Bravo","score":20}""", false), ("d", """{"title":"Delta","score":40}""", true)];
}
