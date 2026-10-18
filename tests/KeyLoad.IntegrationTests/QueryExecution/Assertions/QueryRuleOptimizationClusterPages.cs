using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueryRuleOptimizationClusterPages
{
    private const int ExpectedScanBudget = 10000;

    private const int Routes = 6;
    internal static async Task RunAsync(RequestCqrsClusterCallers reader, QueryRuleOptimizationClusterState state, bool denied, CancellationToken token)
    {
        for (var collection = 0; collection < QueryRuleOptimizationClusterData.Collections.Length; collection++)
        {
            foreach (var shape in new[] { QueryRuleOptimizationClusterData.RevisionOneShape, QueryRuleOptimizationClusterData.OpenRevisionOneShape, QueryRuleOptimizationClusterData.OpenRevisionTwoShape, QueryRuleOptimizationClusterData.RawShadowShape, QueryRuleOptimizationClusterData.NestedShape })
            {
                foreach (var reference in new[] { false, true })
                {
                    await VariantAsync(reader, state, collection, shape, reference, denied, token);
                }
            }
        }
    }
    private static async Task VariantAsync(RequestCqrsClusterCallers reader, QueryRuleOptimizationClusterState state,
        int collection, int shape, bool reference, bool denied, CancellationToken token)
    {
        var ast = QueryRuleOptimizationClusterRequests.Ast(state.Partition, collection, shape, reference);
        var builder = QueryRuleOptimizationClusterRequests.Builder(state.Partition, collection, shape, reference);
        await SqlClusterProtocol.EqualAsync(ast, builder);
        var json = JsonSerializer.Deserialize<AstQueryRequest>(JsonSerializer.Serialize(builder, JsonDefaults.Options), JsonDefaults.Options)!;
        await SqlClusterProtocol.EqualAsync(ast, json);
        var sql = QueryRuleOptimizationClusterRequests.Sql(state.Partition, collection, shape, reference);
        var path = QueryRuleOptimizationClusterData.Path(collection, shape, reference, state.IncludeNull, state.IncludeMissing);
        for (var route = 0; route < Routes; route++)
        {
            if (denied)
            {
                await QueryRuleOptimizationClusterRefusals.CheckAsync(reader, json, sql, route, true, token);
                continue;
            }
            await RowsAsync(reader, json, sql, shape, path, route, token);
            if (path == QueryRuleOptimizationClusterData.Scan)
            {
                var noAstConsent = json with
                {
                    AllowFullScan = false
                };
                var noSqlConsent = sql with
                {
                    AllowFullScan = false
                };
                await QueryRuleOptimizationClusterRefusals.CheckAsync(reader, noAstConsent, noSqlConsent, route, false, token);
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
        await QueryRuleOptimizationClusterRefusals.CheckAsync(reader, plan, sql, QueryRuleOptimizationClusterData.AstSdkRoute, true, token);
        await QueryRuleOptimizationClusterRefusals.CheckAsync(reader, plan, sql, QueryRuleOptimizationClusterData.AstMcpRoute, true, token);
    }
    private static async Task RowsAsync(RequestCqrsClusterCallers reader, AstQueryRequest ast, QueryRequest sql,
        int shape, string path, int route, CancellationToken token)
    {
        var expected = QueryRuleOptimizationClusterData.Expected(shape);
        long? cut = null;
        for (var offset = 0; offset < Math.Max(1, expected.Length); offset += QueryRuleOptimizationClusterData.PageSize)
        {
            var actual = await ReadAsync(reader, ast, sql, route, token);
            cut ??= actual.CutPosition;
            await Assert.That(cut.Value).IsGreaterThan(0L);
            var rows = expected.Skip(offset).Take(QueryRuleOptimizationClusterData.PageSize).ToArray();
            await Assert.That(actual.Cursor is null).IsEqualTo(offset + rows.Length == expected.Length);
            await SqlClusterProtocol.EqualAsync(new QueryPage([.. rows], actual.Cursor, cut.Value, path), actual);
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
    private static async Task<QueryPage> ReadAsync(RequestCqrsClusterCallers reader, AstQueryRequest ast, QueryRequest sql, int route, CancellationToken token)
        => route switch
        {
            QueryRuleOptimizationClusterData.SqlSdkRoute => await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAsync(sql, token)),
            QueryRuleOptimizationClusterData.SqlMcpRoute => await ReadMcpAsync(reader, McpCallerTools.QueryExecute, sql, token),
            QueryRuleOptimizationClusterData.UnifiedSdkRoute => await SqlClusterProtocol.SdkAsync<QueryPage>(reader.Sdk, new(sql.Partition, sql.Sql, sql.Parameters, sql.AllowFullScan, sql.Cursor), token),
            QueryRuleOptimizationClusterData.UnifiedMcpRoute => await SqlClusterProtocol.McpAsync<QueryPage>(reader.Mcp, new(sql.Partition, sql.Sql, sql.Parameters, sql.AllowFullScan, sql.Cursor), token),
            QueryRuleOptimizationClusterData.AstSdkRoute => await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAstAsync(ast, token)),
            QueryRuleOptimizationClusterData.AstMcpRoute => await ReadMcpAsync(reader, McpCallerTools.QueryAst, ast, token),
            _ => throw new InvalidOperationException("The optimizer query route is invalid.")
        };
    private static async Task<QueryPage> ReadMcpAsync<T>(RequestCqrsClusterCallers reader, string tool, T request, CancellationToken token)
        => (await McpCallerAssertions.SuccessAsync<QueryPage>(await reader.Mcp.CallAsync(tool, request, token))).Value;
    private static async Task ExplainAsync(RequestCqrsClusterCallers reader, AstQueryRequest ast, string path, CancellationToken token)
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
            await SqlClusterProtocol.EqualAsync(new QueryPage([new("explain", 0L, body)], null, page.CutPosition, path), page);
        }
    }
}
