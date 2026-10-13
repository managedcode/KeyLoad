using KeyLoad.Query;
namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlFetchCountPages
{
    private const string AccessPath = "bounded-full-scan";
    private const long Revision = 1;
    private const int PageRows = 2;
    internal static async Task HealthyAsync(global::KeyLoad.Core.DatabaseEngine database,
        global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store, PartitionRef partition, string principal)
    {
        var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        await Assert.That(engine.Capabilities.ReadProfiles.Contains(SqlFetchCountProtocol.Profile)).IsTrue();
        var image = SqlInnerJoinRetainedIdentityFlow.Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        foreach (var item in SqlFetchCountProtocol.Cases)
        {
            var actual = engine.Execute(principal, SqlFetchCountProtocol.Sql(partition, item.Tail));
            await CursorAsync(actual, item.Count);
            await EqualAsync(Expected(item.Count, actual.Cursor, position), actual);
            var ast = engine.ExecuteAst(principal, SqlFetchCountProtocol.Ast(partition, item.Count));
            await CursorAsync(ast, item.Count);
            await EqualAsync(Expected(item.Count, ast.Cursor, position), ast);
            await SqlFetchCountRefusals.UnchangedAsync(store, image, position, identity);
        }
        var unordered = engine.Execute(principal, new QueryRequest(partition,
            "SELECT label AS label FROM fetch_count FETCH FIRST ROW ONLY", AllowFullScan: true));
        await CursorAsync(unordered, 1);
        await EqualAsync(Expected(1, unordered.Cursor, position), unordered);
        await SqlFetchCountRefusals.UnchangedAsync(store, image, position, identity);
        var request = SqlFetchCountProtocol.Ast(partition, PageRows);
        var first = engine.Execute(principal, SqlFetchCountProtocol.Sql(partition, "FETCH FIRST 2 ROWS ONLY"));
        await CursorAsync(first, PageRows);
        var final = engine.ExecuteAst(principal, request with { Cursor = first.Cursor });
        var expected = new QueryPage([new("c", Revision, """{"label":"c"}""", RedactedFields: [])], null, position, AccessPath);
        await EqualAsync(expected, final);
        await SqlFetchCountRefusals.UnchangedAsync(store, image, position, identity);
    }
    private static QueryPage Expected(int count, string? cursor, long cut)
    {
        string[] ids = ["a", "b", "c"];
        return new([.. ids.Take(count).Select(id => new QueryRow(id, Revision,
            "{\"label\":\"" + id + "\"}", RedactedFields: []))], cursor, cut, AccessPath);
    }
    private static async Task CursorAsync(QueryPage page, int count)
    {
        if (count < SqlFetchCountProtocol.ResultCount)
        { await Assert.That(page.Cursor).IsNotNull(); }
        else
        { await Assert.That(page.Cursor).IsNull(); }
    }
    internal static async Task EqualAsync(QueryPage expected, QueryPage actual)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    internal static async Task ReplayAsync(global::KeyLoad.Core.DatabaseEngine database, CommandRequest seed, CommitReceipt receipt)
    {
        var operation = new ReplicatedOperation(seed.CommandId, OperationKind.Batch, SqlFetchCountProtocol.Root,
            default, global::System.Text.Json.JsonSerializer.Serialize(seed, JsonDefaults.Options));
        var actual = database.ApplyEmbedded(operation, cancellationToken: default).Get<CommitReceipt>();
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
    }
}
