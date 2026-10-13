using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlOffsetRf3Pages
{
    internal static async Task HealthyAsync(RequestCqrsRf3Callers callers, SqlOffsetRf3State state, CancellationToken token)
    {
        foreach (var window in new[] { "LIMIT 1 OFFSET 1", "OFFSET 1 ROW FETCH FIRST ROW ONLY",
            "OFFSET 1 ROWS FETCH NEXT 1 ROW ONLY", "OFFSET 1 FETCH FIRST 1 ROWS ONLY" })
        { await WindowAsync(callers, state, window, token); }
    }
    private static async Task WindowAsync(RequestCqrsRf3Callers callers, SqlOffsetRf3State state,
        string window, CancellationToken token)
    {
        foreach (var official in new[] { false, true })
        {
            await ScalarAsync(callers, state, official, ast: false, window, token);
            await ScalarAsync(callers, state, official, ast: true, window, token);
            var join = SqlOffsetRf3Protocol.Join(state.Partition);
            var page = official
                ? (await McpCallerAssertions.SuccessAsync<QueryPage>(await callers.Mcp.CallAsync(McpCallerTools.QueryAst, join, token))).Value
                : await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.QueryAstAsync(join, token));
            await JoinAsync(page);
            var sql = SqlOffsetRf3Protocol.JoinRequest(state.Partition) with
            { Sql = SqlOffsetRf3Protocol.JoinSql.Replace("LIMIT 1 OFFSET 1", window, StringComparison.Ordinal) };
            page = official
                ? (await McpCallerAssertions.SuccessAsync<QueryPage>(await callers.Mcp.CallAsync(McpCallerTools.QueryExecute, sql, token))).Value
                : await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.QueryAsync(sql, token));
            await JoinAsync(page);
            var envelope = new SqlOperationRequest(state.Partition, sql.Sql,
                AllowFullScan: true, QueryDialectVersion: SqlOffsetRf3Protocol.Q2);
            var plainEnvelope = new SqlOperationRequest(state.Partition,
                sql.Sql.Replace(" INNER JOIN ", " JOIN ", StringComparison.Ordinal),
                AllowFullScan: true, QueryDialectVersion: SqlOffsetRf3Protocol.Q2);
            await JoinAsync(official ? await SqlRf3Protocol.McpAsync<QueryPage>(callers.Mcp, plainEnvelope, token)
                : await SqlRf3Protocol.SdkAsync<QueryPage>(callers.Sdk, plainEnvelope, token));
            var implicitEnvelope = new SqlOperationRequest(state.Partition,
                sql.Sql.Replace(" AS ", " ", StringComparison.Ordinal),
                AllowFullScan: true, QueryDialectVersion: SqlOffsetRf3Protocol.Q2);
            await JoinAsync(official ? await SqlRf3Protocol.McpAsync<QueryPage>(callers.Mcp, implicitEnvelope, token)
                : await SqlRf3Protocol.SdkAsync<QueryPage>(callers.Sdk, implicitEnvelope, token));
            page = official ? await SqlRf3Protocol.McpAsync<QueryPage>(callers.Mcp, envelope, token)
                : await SqlRf3Protocol.SdkAsync<QueryPage>(callers.Sdk, envelope, token);
            await JoinAsync(page);
        }
    }
    private static async Task ScalarAsync(RequestCqrsRf3Callers callers, SqlOffsetRf3State state,
        bool official, bool ast, string window, CancellationToken token)
    {
        string? cursor = null;
        long? cut = null;
        foreach (var (id, json, final) in new[]
        { ("b", """{"title":"second"}""", false), ("c", """{"title":"third"}""", false), ("d", """{"title":"fourth"}""", true) })
        {
            var sql = SqlOffsetRf3Protocol.ScalarRequest(state.Partition) with
            { Cursor = cursor, Sql = SqlOffsetRf3Protocol.ScalarSql.Replace("LIMIT 1 OFFSET 1", window, StringComparison.Ordinal) };
            var request = SqlOffsetRf3Protocol.Scalar(state.Partition) with { Cursor = cursor };
            var page = ast
                ? official
                    ? (await McpCallerAssertions.SuccessAsync<QueryPage>(await callers.Mcp.CallAsync(McpCallerTools.QueryAst, request, token))).Value
                    : await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.QueryAstAsync(request, token))
                : official
                    ? (await McpCallerAssertions.SuccessAsync<QueryPage>(await callers.Mcp.CallAsync(McpCallerTools.QueryExecute, sql, token))).Value
                    : await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.QueryAsync(sql, token));
            // The original signed cursor binds logical snapshot continuation; physical-owner correlation remains a separate RF3 gate.
            cut ??= page.CutPosition;
            await Assert.That(page.CutPosition).IsEqualTo(cut.Value);
            if (final)
            { await Assert.That(page.Cursor).IsNull(); }
            else
            { await Assert.That(page.Cursor).IsNotNull(); }
            await SqlRf3Protocol.EqualAsync(new QueryPage(
                [new(id, SqlOffsetRf3Protocol.InitialRevision, json, false, [])],
                page.Cursor, cut.Value, "bounded-full-scan"), page);
            cursor = page.Cursor;
        }
    }
    private static async Task JoinAsync(QueryPage page)
    {
        await SqlRf3Protocol.EqualAsync(new QueryPage(
            [new("b", SqlOffsetRf3Protocol.InitialRevision, """{"title":"second","name":"Bee"}""", false, [],
                [new(SqlOffsetRf3Protocol.LeftAlias, "b", SqlOffsetRf3Protocol.InitialRevision),
                 new(SqlOffsetRf3Protocol.RightAlias, "c2", SqlOffsetRf3Protocol.InitialRevision)])],
            null, page.CutPosition, "bounded-primary-key-inner-join"), page);
    }
}
