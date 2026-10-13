namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlRadixIntegerControls
{
    internal static async Task InvalidAsync(TestDatabase database, global::KeyLoad.Query.QueryEngine engine)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        foreach (var clause in new[] { "score = 0b",
            "score = 0B2",
            "score = 0o",
            "score = 0O8",
            "score = 0b1000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000",
            "score = -0b1000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000",
            "score = 0o100000000000000000000000000000000",
            "score = -0o100000000000000000000000000000000" })
        {
            await RefuseAsync(database, engine, SqlNullDistinctCases.Prefix + clause + SqlNullDistinctCases.Tail,
            ErrorCode.Validation, image, position, identity);
        }
        foreach (var clause in new[] { "score = 0b1.0",
            "score = 0o1.0",
            "score = 0b1_0",
            "score = 0o1_0",
            "score = 0b102",
            "score = 0o128" })
        {
            await RefuseAsync(database, engine, SqlNullDistinctCases.Prefix + clause + SqlNullDistinctCases.Tail,
            ErrorCode.UnsupportedCapability, image, position, identity);
        }
        foreach (var window in new[] { "LIMIT 0b1",
            "LIMIT 0o1",
            "OFFSET 0b1 LIMIT 1",
            "OFFSET 0o1 LIMIT 1",
            "FETCH FIRST 0b1 ROW ONLY",
            "FETCH FIRST 0o1 ROW ONLY",
            "LIMIT +0b1",
            "OFFSET -0o1 LIMIT 1",
            "FETCH FIRST +0o1 ROW ONLY" })
        {
            await RefuseAsync(database, engine, SqlNullDistinctCases.Prefix + SqlNullDistinctCases.PositiveClause
            + OrderBy + window, ErrorCode.Validation, image, position, identity);
        }
        await CancelledAsync(database, engine, image, position, identity);
    }

    private static async Task RefuseAsync(TestDatabase database, global::KeyLoad.Query.QueryEngine engine,
        string sql, ErrorCode code, string[] image, long position, byte[] identity)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlNullOrderingProtocol.Reader,
            new(database.Partition, sql, AllowFullScan: true)));
        await Assert.That(error.Code).IsEqualTo(code);
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
        await SqlNullDistinctPages.HealthyAsync(database.Database, database.Store, database.Partition,
            SqlNullDistinctRefusals.InitialRevision);
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
    }

    private static async Task CancelledAsync(TestDatabase database, global::KeyLoad.Query.QueryEngine engine,
        string[] image, long position, byte[] identity)
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var originalToken = cancelled.Token;
        foreach (var clause in new[] { SqlNullDistinctCases.BinaryClause, SqlNullDistinctCases.OctalClause })
        {
            var request = new QueryRequest(database.Partition, SqlNullDistinctCases.Prefix + clause
                + SqlNullDistinctCases.Tail, AllowFullScan: true);
            var error = Assert.ThrowsExactly<OperationCanceledException>(() => engine.Execute(SqlNullOrderingProtocol.Reader,
                request, cancellationToken: originalToken));
            await Assert.That(error.CancellationToken).IsEqualTo(originalToken);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
            using var healthy = new CancellationTokenSource();
            var healthyToken = healthy.Token;
            await Assert.That(healthyToken.IsCancellationRequested).IsFalse();
            var expected = new global::KeyLoad.QueryPage([new global::KeyLoad.QueryRow(Low,
                SqlNullDistinctRefusals.InitialRevision, LowProjection, RedactedFields: [])], null, position, BoundedFullScan);
            await SqlNullOrderingPages.EqualAsync(expected, engine.Execute(SqlNullOrderingProtocol.Reader,
                request, cancellationToken: healthyToken));
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
        }
    }

    private const string Low = "low";
    private const string LowProjection = "{\"label\":\"low\"}";
    private const string BoundedFullScan = "bounded-full-scan";
    private const string OrderBy = " ORDER BY label ASC ";
}
