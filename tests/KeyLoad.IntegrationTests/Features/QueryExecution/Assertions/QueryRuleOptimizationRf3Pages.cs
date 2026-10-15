using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueryRuleOptimizationRf3Pages
{
    private const int ExpectedScanBudget = 10000;

    private const int Routes = 6;
    internal static async Task RunAsync(RequestCqrsRf3Callers reader, QueryRuleOptimizationRf3State state, bool denied, CancellationToken token)
    {
        for (var collection = 0; collection < QueryRuleOptimizationRf3Data.Collections.Length; collection++)
        {
            foreach (var shape in new[] { QueryRuleOptimizationRf3Data.RevisionOneShape, QueryRuleOptimizationRf3Data.OpenRevisionOneShape, QueryRuleOptimizationRf3Data.OpenRevisionTwoShape, QueryRuleOptimizationRf3Data.RawShadowShape, QueryRuleOptimizationRf3Data.NestedShape })
            {
                foreach (var reference in new[] { false, true })
                {
                    await VariantAsync(reader, state, collection, shape, reference, denied, token);
                }
            }
        }
    }
    private static async Task VariantAsync(RequestCqrsRf3Callers reader, QueryRuleOptimizationRf3State state,
        int collection, int shape, bool reference, bool denied, CancellationToken token)
    {
        var ast = QueryRuleOptimizationRf3Requests.Ast(state.Partition, collection, shape, reference);
        var builder = QueryRuleOptimizationRf3Requests.Builder(state.Partition, collection, shape, reference);
        await SqlRf3Protocol.EqualAsync(ast, builder);
        var json = JsonSerializer.Deserialize<AstQueryRequest>(JsonSerializer.Serialize(builder, JsonDefaults.Options), JsonDefaults.Options)!;
        await SqlRf3Protocol.EqualAsync(ast, json);
        var sql = QueryRuleOptimizationRf3Requests.Sql(state.Partition, collection, shape, reference);
        var path = QueryRuleOptimizationRf3Data.Path(collection, shape, reference, state.IncludeNull, state.IncludeMissing);
        for (var route = 0; route < Routes; route++)
        {
            if (denied)
            {
                await QueryRuleOptimizationRf3Refusals.CheckAsync(reader, json, sql, route, true, token);
                continue;
            }
            await RowsAsync(reader, json, sql, shape, path, route, token);
            if (path == QueryRuleOptimizationRf3Data.Scan)
            {
                var noAstConsent = json with
                {
                    AllowFullScan = false
                };
                var noSqlConsent = sql with
                {
                    AllowFullScan = false
                };
                await QueryRuleOptimizationRf3Refusals.CheckAsync(reader, noAstConsent, noSqlConsent, route, false, token);
            }
        }
        if (!denied)
        {
            await ExplainAsync(reader, json, path, token);
            return;
        }
        var plan = json with
        {
            Query = json.Query with
            {
                Explain = true
            }
        };
        await QueryRuleOptimizationRf3Refusals.CheckAsync(reader, plan, sql, QueryRuleOptimizationRf3Data.AstSdkRoute, true, token);
        await QueryRuleOptimizationRf3Refusals.CheckAsync(reader, plan, sql, QueryRuleOptimizationRf3Data.AstMcpRoute, true, token);
    }
    private static async Task RowsAsync(RequestCqrsRf3Callers reader, AstQueryRequest ast, QueryRequest sql,
        int shape, string path, int route, CancellationToken token)
    {
        var expected = QueryRuleOptimizationRf3Data.Expected(shape);
        long? cut = null;
        for (var offset = 0; offset < Math.Max(1, expected.Length); offset += QueryRuleOptimizationRf3Data.PageSize)
        {
            var actual = await ReadAsync(reader, ast, sql, route, token);
            cut ??= actual.CutPosition;
            await Assert.That(cut.Value).IsGreaterThan(0L);
            var rows = expected.Skip(offset).Take(QueryRuleOptimizationRf3Data.PageSize).ToArray();
            await Assert.That(actual.Cursor is null).IsEqualTo(offset + rows.Length == expected.Length);
            await SqlRf3Protocol.EqualAsync(new QueryPage([.. rows], actual.Cursor, cut.Value, path), actual);
            ast = ast with
            {
                Cursor = actual.Cursor
            };
            sql = sql with
            {
                Cursor = actual.Cursor
            };
        }
    }
    private static async Task<QueryPage> ReadAsync(RequestCqrsRf3Callers reader, AstQueryRequest ast, QueryRequest sql, int route, CancellationToken token)
        => route switch
        {
            QueryRuleOptimizationRf3Data.SqlSdkRoute => await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAsync(sql, token)),
            QueryRuleOptimizationRf3Data.SqlMcpRoute => await ReadMcpAsync(reader, McpCallerTools.QueryExecute, sql, token),
            QueryRuleOptimizationRf3Data.UnifiedSdkRoute => await SqlRf3Protocol.SdkAsync<QueryPage>(reader.Sdk, new(sql.Partition, sql.Sql, sql.Parameters, sql.AllowFullScan, sql.Cursor), token),
            QueryRuleOptimizationRf3Data.UnifiedMcpRoute => await SqlRf3Protocol.McpAsync<QueryPage>(reader.Mcp, new(sql.Partition, sql.Sql, sql.Parameters, sql.AllowFullScan, sql.Cursor), token),
            QueryRuleOptimizationRf3Data.AstSdkRoute => await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAstAsync(ast, token)),
            QueryRuleOptimizationRf3Data.AstMcpRoute => await ReadMcpAsync(reader, McpCallerTools.QueryAst, ast, token),
            _ => throw new InvalidOperationException("The optimizer query route is invalid.")
        };
    private static async Task<QueryPage> ReadMcpAsync<T>(RequestCqrsRf3Callers reader, string tool, T request, CancellationToken token)
        => (await McpCallerAssertions.SuccessAsync<QueryPage>(await reader.Mcp.CallAsync(tool, request, token))).Value;
    private static async Task ExplainAsync(RequestCqrsRf3Callers reader, AstQueryRequest ast, string path, CancellationToken token)
    {
        var plan = ast with
        {
            Query = ast.Query with
            {
                Explain = true
            }
        };
        var pages = new[]
        {
            await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAstAsync(plan, token)),
            (await McpCallerAssertions.SuccessAsync<QueryPage>(await reader.Mcp.CallAsync(McpCallerTools.QueryAst, plan, token))).Value
        };
        var body = JsonSerializer.Serialize(new
        {
            accessPath = path,
            atomicPartition = ast.Partition.AtomicPartitionId,
            scanBudget = ExpectedScanBudget
        }, JsonDefaults.Options);
        foreach (var page in pages)
        {
            await Assert.That(page.CutPosition).IsGreaterThan(0L);
            await SqlRf3Protocol.EqualAsync(new QueryPage([new("explain", 0L, body)], null, page.CutPosition, path), page);
        }
    }
}
