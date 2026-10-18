using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlParserAdmissionClusterReads
{
    internal const string Plain = "SELECT TOP ((1)) title FROM offsetleft ORDER BY order_id";
    internal static async Task HealthyAsync(RequestCqrsClusterCallers reader, SqlParserAdmissionClusterState state, CancellationToken token)
    {
        for (var route = 0; route < 6; route++)
        {
            var sql = Plain;
            string? cursor = null;
            long? cut = null;
            foreach (var (id, json) in new[] { ("a", """{"title":"first"}"""), ("b", """{"title":"second"}"""),
                ("c", """{"title":"third"}"""), ("d", """{"title":"fourth"}""") })
            {
                var ast = new AstQueryRequest(state.Partition, new(SqlOffsetClusterProtocol.Left, null, [new("/title", "title")], null,
                    [new("/order_id", false)], 1), AllowFullScan: true, Cursor: cursor);
                var page = await ReadAsync(reader, state.Partition, sql, cursor, route, token, ast);
                cut ??= page.CutPosition;
                await Assert.That(cut.Value).IsGreaterThan(0L);
                await Assert.That(page.Cursor is null).IsEqualTo(id == "d");
                await SqlClusterProtocol.EqualAsync(new QueryPage([new(id, 1L, json, false, [])], page.Cursor, cut.Value, "bounded-full-scan"), page);
                cursor = page.Cursor;
            }
            await ModelsAsync(reader, state, route, token);
        }
    }
    internal static async Task<QueryPage> ReadAsync(RequestCqrsClusterCallers reader, PartitionRef partition, string sql,
        string? cursor, int route, CancellationToken token, AstQueryRequest? ast = null)
    {
        var query = new QueryRequest(partition, sql, AllowFullScan: true, Cursor: cursor);
        var envelope = new SqlOperationRequest(partition, sql, AllowFullScan: true, Cursor: cursor);
        return route switch
        {
            0 => await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAsync(query, token)),
            1 => (await McpCallerAssertions.SuccessAsync<QueryPage>(await reader.Mcp.CallAsync(McpCallerTools.QueryExecute, query, token))).Value,
            2 => await SqlClusterProtocol.SdkAsync<QueryPage>(reader.Sdk, envelope, token),
            3 => await SqlClusterProtocol.McpAsync<QueryPage>(reader.Mcp, envelope, token),
            4 => await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAstAsync(ast ?? throw new InvalidOperationException("The typed control is required."), token)),
            5 => (await McpCallerAssertions.SuccessAsync<QueryPage>(await reader.Mcp.CallAsync(McpCallerTools.QueryAst,
                ast ?? throw new InvalidOperationException("The typed control is required."), token))).Value,
            _ => throw new InvalidOperationException("The parser control route is invalid.")
        };
    }
    private static async Task ModelsAsync(RequestCqrsClusterCallers reader, SqlParserAdmissionClusterState state, int route, CancellationToken token)
    {
        var sql = "SELECT TOP (10) payload.value AS value FROM EVENTS('parser-events','source',+1) ORDER BY revision";
        var ast = new AstQueryRequest(state.Partition, new(SqlParserAdmissionClusterState.EventResource, null, [new("/payload/value", "value")], null,
            [new("/revision", false)], 10, ModelSource: new(ModelQuerySourceKind.Events, "source", 1L)), AllowFullScan: true);
        var page = await ReadAsync(reader, state.Partition, sql, null, route, token, ast);
        await Assert.That(page.CutPosition).IsGreaterThan(0L);
        await SqlClusterProtocol.EqualAsync(new QueryPage([new("created", 1L, "{\"value\":1}", false, []), new("updated", 2L, "{\"value\":2}", false, [])],
            null, page.CutPosition, "model-scan:events"), page);
        var typedAst = new AstQueryRequest(state.Partition, new(SqlParserAdmissionClusterState.Table, null, [new("/amount", "amount")], null,
            [new("/@id", false)], 1), AllowFullScan: true);
        var typed = await ReadAsync(reader, state.Partition, "SELECT TOP (1) amount FROM [parser-typed] ORDER BY id", null, route, token, typedAst);
        await Assert.That(typed.CutPosition).IsGreaterThan(0L);
        await SqlClusterProtocol.EqualAsync(new QueryPage([new("typed-row", 1L, """{"amount":-1}""", false, [])],
            null, typed.CutPosition, "bounded-full-scan"), typed);
    }
}
