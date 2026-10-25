using System.Text;
using KeyLoad.FunctionalTests.Authorization;
using KeyLoad.FunctionalTests.GraphTraversal;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class SqlParserAdmissionUnicode
{
    private const string Prefix = "/*🙂界*/\r\n";
    private const string Plain = "SELECT TOP ((1)) title FROM offsetleft ORDER BY order_id";
    private const string Open = " /*🙂界";
    private const string Close = "*/";
    private const string Malformed = "SELECT title FROM offsetleft WHERE title = N'PRIVATE-PARSER-ARGUMENT🙂' AND ;";
    private const int ByteBound = 4096;
    private const int StatementCount = 1;
    private const int SecondLine = 2;
    private const int FirstColumn = 1;
    private const char Padding = 'x';
    private const char Terminator = ';';

    internal static Task RunAsync(SqlParserAdmissionFixture fixture, DatabaseEngine database, ZoneTreeStore store, CancellationToken token)
        => GraphLabelUseOperations.ObserveAsync(store, async () =>
        {
            await OffsetsAsync();
            var exact = Prefix + Plain + Open;
            exact += new string(Padding, ByteBound - Encoding.UTF8.GetByteCount(exact + Close)) + Close;
            await Assert.That(Encoding.UTF8.GetByteCount(exact)).IsEqualTo(ByteBound);
            await Assert.That(exact.Length).IsLessThan(ByteBound);
            await PagesAsync(fixture, database, store, exact, token);
            await SqlParserAdmissionReads.RefuseAsync(fixture, database, store, Prefix + Malformed,
                ErrorCode.Validation, "The SQL syntax or query arguments are invalid.", token);
            await PagesAsync(fixture, database, store, exact, token);
        });

    private static async Task OffsetsAsync()
    {
        var parser = new Dom.TSql180Parser(initialQuotedIdentifiers: true, Dom.SqlEngineType.Standalone);
        using var input = new StringReader(Prefix + Plain);
        var script = (Dom.TSqlScript)parser.Parse(input, out var errors);
        await Assert.That(errors).IsEmpty();
        await Assert.That(script.Batches.Count).IsEqualTo(StatementCount);
        await Assert.That(script.Batches[0].Statements.Count).IsEqualTo(StatementCount);
        var statement = script.Batches[0].Statements[0];
        await Assert.That(statement.StartOffset).IsEqualTo(Prefix.Length);
        await Assert.That(statement.StartLine).IsEqualTo(SecondLine);
        await Assert.That(statement.StartColumn).IsEqualTo(FirstColumn);
        await Assert.That(Encoding.UTF8.GetByteCount(Prefix)).IsGreaterThan(Prefix.Length);
        var malformed = Prefix + Malformed;
        using var invalid = new StringReader(malformed);
        _ = parser.Parse(invalid, out var denied);
        await Assert.That(denied).IsNotEmpty();
        await Assert.That(denied[0].Offset).IsEqualTo(malformed.LastIndexOf(Terminator));
        await Assert.That(denied[0].Line).IsEqualTo(SecondLine);
        await Assert.That(denied[0].Column).IsEqualTo(Malformed.LastIndexOf(Terminator) + FirstColumn);
    }

    private static async Task PagesAsync(SqlParserAdmissionFixture fixture, DatabaseEngine database, ZoneTreeStore store,
        string sql, CancellationToken token)
    {
        var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        var request = new QueryRequest(fixture.Harness.Partition, sql, AllowFullScan: true);
        foreach (var (id, json) in new[]
        {
            ("a", """{"title":"first"}"""), ("b", """{"title":"second"}"""),
            ("c", """{"title":"third"}"""), ("d", """{"title":"fourth"}""")
        })
        {
            var page = engine.Execute(SqlOffsetProtocol.Reader, request, cancellationToken: token);
            await Assert.That(page.Cursor is null).IsEqualTo(id == "d");
            await RequiredProcessingPolicyAssertions.EqualAsync(new QueryPage([new(id, 1L, json, false, [])],
                page.Cursor, store.Position, "bounded-full-scan"), page);
            request = request with
            {
                Cursor = page.Cursor
            };
        }
    }
}
