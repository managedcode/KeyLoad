using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Authorization;
using KeyLoad.UnitTests.GraphTraversal;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class SqlParserAdmissionNumericControls
{
    internal static Task RunAsync(SqlParserAdmissionFixture fixture, DatabaseEngine database, ZoneTreeStore store, CancellationToken token)
        => GraphLabelUseOperations.ObserveAsync(store, async () =>
        {
            var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
            foreach (var control in Controls)
            {
                var sql = new QueryRequest(fixture.Harness.Partition, control.Sql, AllowFullScan: true);
                var ast = new AstQueryRequest(fixture.Harness.Partition, new(SqlOffsetProtocol.Left, null, [new("/title", "title")], control.Filter,
                    [new(control.Path, false)], 1, Offset: control.Offset), AllowFullScan: true);
                foreach (var row in control.Rows)
                {
                    var page = engine.Execute(SqlOffsetProtocol.Reader, sql, cancellationToken: token);
                    var typed = engine.ExecuteAst(SqlOffsetProtocol.Reader, ast, cancellationToken: token);
                    var final = row.Id == control.Rows[^1].Id;
                    await PageAsync(page, row, store.Position, final);
                    await PageAsync(typed, row, store.Position, final);
                    sql = sql with
                    {
                        Cursor = page.Cursor
                    };
                    ast = ast with
                    {
                        Cursor = typed.Cursor
                    };
                }
            }
        });
    private static async Task PageAsync(QueryPage page, (string Id, string Json) row, long cut, bool final)
    {
        await Assert.That(page.Cursor is null).IsEqualTo(final);
        await RequiredProcessingPolicyAssertions.EqualAsync(new QueryPage([new(row.Id, 1L, row.Json, false, [])], page.Cursor, cut, "bounded-full-scan"), page);
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
