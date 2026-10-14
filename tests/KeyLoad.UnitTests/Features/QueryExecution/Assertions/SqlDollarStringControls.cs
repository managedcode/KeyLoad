namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlDollarStringControls
{
    internal static async Task InvalidAsync(TestDatabase database, global::KeyLoad.Query.QueryEngine engine)
    {
        var before = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        foreach (var clause in new[] { "label = $tag$unfinished", "label = $TAG$low$tag$",
            "label = $$lo\0w$$", "label = $tag-low$low$tag-low$" })
        {
            await RefuseAsync(database, engine, clause, ErrorCode.Validation, before, position, identity);
        }
        foreach (var clause in new[] { "label = $$lo$$\n$$w$$", "label = $a$low$a$ $a$other$a$" })
        {
            await RefuseAsync(database, engine, clause, ErrorCode.Validation, before, position, identity);
        }
        await CancelledAsync(database, engine, before, position, identity);
    }

    private static async Task CancelledAsync(TestDatabase database, global::KeyLoad.Query.QueryEngine engine,
        string[] before, long position, byte[] identity)
    {
        var request = new QueryRequest(database.Partition, SqlNullDistinctCases.Prefix
            + SqlNullDistinctCases.DollarClause + SqlNullDistinctCases.Tail, AllowFullScan: true);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var originalToken = cancelled.Token;
        var error = Assert.ThrowsExactly<OperationCanceledException>(() => engine.Execute(SqlNullOrderingProtocol.Reader,
            request, cancellationToken: originalToken));
        await Assert.That(error.CancellationToken).IsEqualTo(originalToken);
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
        using var healthy = new CancellationTokenSource();
        var healthyToken = healthy.Token;
        await Assert.That(healthyToken.IsCancellationRequested).IsFalse();
        var expected = new global::KeyLoad.QueryPage([new global::KeyLoad.QueryRow(Low,
            SqlNullDistinctRefusals.InitialRevision, LowProjection, RedactedFields: [])], null, position, BoundedFullScan);
        await SqlNullOrderingPages.EqualAsync(expected, engine.Execute(SqlNullOrderingProtocol.Reader,
            request, cancellationToken: healthyToken));
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
        await SqlNullDistinctPages.HealthyAsync(database.Database, database.Store, database.Partition,
            SqlNullDistinctRefusals.InitialRevision);
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
    }

    private static async Task RefuseAsync(TestDatabase database, global::KeyLoad.Query.QueryEngine engine,
        string clause, ErrorCode code, string[] before, long position, byte[] identity)
    {
        var sql = SqlNullDistinctCases.Prefix + clause + SqlNullDistinctCases.Tail;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlNullOrderingProtocol.Reader,
            new(database.Partition, sql, AllowFullScan: true)));
        await Assert.That(error.Code).IsEqualTo(code);
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
        await SqlNullDistinctPages.HealthyAsync(database.Database, database.Store, database.Partition,
            SqlNullDistinctRefusals.InitialRevision);
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
    }
    private const string Low = "low";
    private const string LowProjection = "{\"label\":\"low\"}";
    private const string BoundedFullScan = "bounded-full-scan";
}
