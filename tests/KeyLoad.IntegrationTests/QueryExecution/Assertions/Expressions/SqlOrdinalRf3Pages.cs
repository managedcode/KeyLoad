using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlOrdinalRf3Pages
{
    internal static async Task HealthyAsync(RequestCqrsRf3Callers caller, SqlOffsetRf3State state,
        PostgresRf3Caller wire, CancellationToken token)
    {
        foreach (var descending in new[] { false, true })
        {
            var rows = descending ? Reverse : Forward;
            foreach (var transport in new[] { SdkSql, McpSql, SdkAst, McpAst })
            {
                await TransportPagesAsync(caller, state, descending, transport, rows, token);
            }
            await WireAsync(wire, descending, rows, token);
        }
    }
    private static async Task TransportPagesAsync(RequestCqrsRf3Callers caller, SqlOffsetRf3State state,
        bool descending, int transport, (string Id, string Json, bool Final)[] rows, CancellationToken token)
    {
        string? cursor = null;
        long? cut = null;
        foreach (var (id, json, final) in rows)
        {
            var page = await ReadAsync(caller, state, descending, transport, cursor, token);
            cut ??= page.CutPosition;
            await global::TUnit.Assertions.Assert.That(page.CutPosition).IsEqualTo(cut.Value);
            if (final)
            {
                await global::TUnit.Assertions.Assert.That(page.Cursor).IsNull();
            }
            else
            {
                await global::TUnit.Assertions.Assert.That(page.Cursor).IsNotNull();
            }
            await SqlRf3Protocol.EqualAsync(new QueryPage(
                [new(id, SqlOffsetRf3Protocol.InitialRevision, json, false, [])],
                page.Cursor, cut.Value, FullScan), page);
            cursor = page.Cursor;
        }
    }
    private static async Task<QueryPage> ReadAsync(RequestCqrsRf3Callers caller, SqlOffsetRf3State state,
        bool descending, int transport, string? cursor, CancellationToken token)
    {
        var sql = "SELECT title, order_id FROM offsetleft ORDER BY 2" + (descending ? " DESC" : " ASC")
            + " OFFSET 1 ROWS FETCH NEXT 1 ROWS ONLY";
        var query = new QueryRequest(state.Partition, sql, AllowFullScan: true, Cursor: cursor);
        var ast = new global::KeyLoad.Query.AstQueryRequest(state.Partition,
            new(SqlOffsetRf3Protocol.Left, null, [new("/title", "title"), new("/order_id", "order_id")],
                null, [new("/order_id", descending)], PageSize, Offset: InitialOffset),
            AllowFullScan: true, Cursor: cursor);
        return transport switch
        {
            SdkSql => await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryAsync(query, token)),
            McpSql => (await McpCallerAssertions.SuccessAsync<QueryPage>(
                await caller.Mcp.CallAsync(McpCallerTools.QueryExecute, query, token))).Value,
            SdkAst => await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryAstAsync(ast, token)),
            _ => (await McpCallerAssertions.SuccessAsync<QueryPage>(
                await caller.Mcp.CallAsync(McpCallerTools.QueryAst, ast, token))).Value
        };
    }
    private static async Task WireAsync(PostgresRf3Caller wire, bool descending,
        (string Id, string Json, bool Final)[] rows, CancellationToken token)
    {
        var sql = "SELECT title, order_id FROM offsetleft ORDER BY 2" + (descending ? " DESC" : " ASC")
            + " OFFSET 1 ROWS FETCH NEXT 3 ROWS ONLY";
        foreach (var prepared in new[] { false, true })
        {
            var actual = await wire.ExecuteAsync<QueryPage>(sql, [], prepared, token);
            await SqlRf3Protocol.EqualAsync(new QueryPage(
                rows.Select(row => new QueryRow(row.Id, SqlOffsetRf3Protocol.InitialRevision,
                    row.Json, false, [])).ToImmutableArray(), null, actual.CutPosition, FullScan), actual);
        }
    }
    private const int SdkSql = 0, McpSql = 1, SdkAst = 2, McpAst = 3;
    private const int PageSize = 1, InitialOffset = 1;
    private const string FullScan = "bounded-full-scan";
    private static readonly (string Id, string Json, bool Final)[] Forward =
        [("b", """{"title":"second","order_id":"b"}""", false),
         ("c", """{"title":"third","order_id":"c"}""", false),
         ("d", """{"title":"fourth","order_id":"d"}""", true)];
    private static readonly (string Id, string Json, bool Final)[] Reverse =
        [("c", """{"title":"third","order_id":"c"}""", false),
         ("b", """{"title":"second","order_id":"b"}""", false),
         ("a", """{"title":"first","order_id":"a"}""", true)];
}
