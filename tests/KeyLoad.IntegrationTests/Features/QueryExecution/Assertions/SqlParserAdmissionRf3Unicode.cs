using System.Text;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlParserAdmissionRf3Unicode
{
    private const string Open = "/*🙂界*/\r\nSELECT TOP ((1)) title FROM offsetleft ORDER BY order_id /*🙂界";
    private const string Close = "*/";
    private const char Padding = 'x';
    private const int ByteBound = 65536;
    private const int SqlRoutes = 4;

    internal static async Task RunAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SqlParserAdmissionRf3State state, CancellationToken token)
    {
        var exact = Open + new string(Padding, ByteBound - Encoding.UTF8.GetByteCount(Open + Close)) + Close;
        await Assert.That(Encoding.UTF8.GetByteCount(exact)).IsEqualTo(ByteBound);
        await Assert.That(exact.Length).IsLessThan(ByteBound);
        for (var route = 0; route < SqlRoutes; route++)
        {
            var currentRoute = route;
            await SqlParserAdmissionRf3Current.ObserveAsync(root, state,
                () => PagesAsync(reader, state, exact, currentRoute, token), token);
        }
        await SqlParserAdmissionRf3Current.ObserveAsync(root, state, () => SqlParserAdmissionRf3Refusals.QueryAsync(
            reader, state, exact + Padding, ErrorCode.BudgetExceeded, "The SQL text exceeds its byte budget.", false, token), token);
        for (var route = 0; route < SqlRoutes; route++)
        {
            var currentRoute = route;
            await SqlParserAdmissionRf3Current.ObserveAsync(root, state,
                () => PagesAsync(reader, state, exact, currentRoute, token), token);
        }
    }

    private static async Task PagesAsync(RequestCqrsRf3Callers reader, SqlParserAdmissionRf3State state,
        string sql, int route, CancellationToken token)
    {
        string? cursor = null;
        long? cut = null;
        foreach (var (id, json) in new[]
        {
            ("a", """{"title":"first"}"""), ("b", """{"title":"second"}"""),
            ("c", """{"title":"third"}"""), ("d", """{"title":"fourth"}""")
        })
        {
            var page = await SqlParserAdmissionRf3Reads.ReadAsync(reader, state.Partition, sql, cursor, route, token);
            cut ??= page.CutPosition;
            await Assert.That(cut.Value).IsGreaterThan(0L);
            await Assert.That(page.Cursor is null).IsEqualTo(id == "d");
            await SqlRf3Protocol.EqualAsync(new QueryPage([new(id, 1L, json, false, [])], page.Cursor,
                cut.Value, "bounded-full-scan"), page);
            cursor = page.Cursor;
        }
    }
}
