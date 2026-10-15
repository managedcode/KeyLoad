using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlParserAdmissionRf3NumericControls
{
    internal static async Task RunAsync(RequestCqrsRf3Callers reader, SqlParserAdmissionRf3State state, CancellationToken token)
    {
        foreach (var control in Controls)
        {
            for (var route = 0; route < 6; route++)
            {
                await PageControlAsync(reader, state, control, route, token);
            }
        }
    }
    private static async Task PageControlAsync(RequestCqrsRf3Callers reader, SqlParserAdmissionRf3State state,
        (string Sql, string Path, int Offset, Predicate? Filter, (string Id, string Json)[] Rows) control, int route, CancellationToken token)
    {
        string? cursor = null;
        long? cut = null;
        foreach (var row in control.Rows)
        {
            var ast = new AstQueryRequest(state.Partition, new(SqlOffsetRf3Protocol.Left, null, [new("/title", "title")], control.Filter,
                [new(control.Path, false)], 1, Offset: control.Offset), AllowFullScan: true, Cursor: cursor);
            var page = await SqlParserAdmissionRf3Reads.ReadAsync(reader, state.Partition, control.Sql, cursor, route, token, ast);
            cut ??= page.CutPosition;
            await Assert.That(cut.Value).IsGreaterThan(0L);
            await Assert.That(page.Cursor is null).IsEqualTo(row.Id == control.Rows[^1].Id);
            await SqlRf3Protocol.EqualAsync(new QueryPage([new(row.Id, 1L, row.Json, false, [])], page.Cursor, cut.Value, "bounded-full-scan"), page);
            cursor = page.Cursor;
        }
    }
    private static (string Sql, string Path, int Offset, Predicate? Filter, (string Id, string Json)[] Rows)[] Controls =>
    [
        ("SELECT TOP (+1) title FROM offsetleft ORDER BY order_id", "/order_id", 0, null,
            [("a", """{"title":"first"}"""), ("b", """{"title":"second"}"""), ("c", """{"title":"third"}"""), ("d", """{"title":"fourth"}""")]),
        ("SELECT TOP (1) title FROM offsetleft ORDER BY 1", "/title", 0, null,
            [("a", """{"title":"first"}"""), ("d", """{"title":"fourth"}"""), ("b", """{"title":"second"}"""), ("c", """{"title":"third"}""")]),
        ("SELECT title FROM offsetleft ORDER BY order_id OFFSET ((1)) ROWS FETCH NEXT 1 ROWS ONLY", "/order_id", 1, null,
            [("b", """{"title":"second"}"""), ("c", """{"title":"third"}"""), ("d", """{"title":"fourth"}""")]),
        ("SELECT TOP (1) title FROM offsetleft WHERE -1 = -1 ORDER BY order_id", "/order_id", 0,
            new Comparison(ValueOperand.Create(-1m), "=", ValueOperand.Create(-1m)),
            [("a", """{"title":"first"}"""), ("b", """{"title":"second"}"""), ("c", """{"title":"third"}"""), ("d", """{"title":"fourth"}""")])
    ];
}
