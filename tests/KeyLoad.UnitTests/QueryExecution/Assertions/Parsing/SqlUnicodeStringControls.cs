namespace KeyLoad.UnitTests.QueryExecution;

internal static class SqlUnicodeStringControls
{
    internal static async Task InvalidAsync(TestDatabase database, global::KeyLoad.Query.QueryEngine engine)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        foreach (var (clause, code) in new[]
        { ("label = U&'\\123'", ErrorCode.UnsupportedCapability),
            ("label = U&'\\+110000'", ErrorCode.UnsupportedCapability),
            ("label = U&'\\D800'", ErrorCode.UnsupportedCapability),
            ("label = U&'\\DC00'", ErrorCode.UnsupportedCapability),
            ("label = U&'\\D800x\\DC00'", ErrorCode.UnsupportedCapability),
            ("label = U&'\\0000'", ErrorCode.UnsupportedCapability),
            ("label = U&'\\+000000'", ErrorCode.UnsupportedCapability),
            ("label = U&'\\g000'", ErrorCode.UnsupportedCapability),
            ("label = U&'unterminated", ErrorCode.Validation),
            ("label = U&'low' UESCAPE", ErrorCode.Validation),
            ("label = U&'low' UESCAPE ''", ErrorCode.Validation),
            ("label = U&'low' UESCAPE '!!'", ErrorCode.Validation),
            ("label = U&'low' UESCAPE '+'", ErrorCode.Validation),
            ("label = U&'low' UESCAPE 'a'", ErrorCode.Validation),
            ("label = U&'low' UESCAPE ' '", ErrorCode.Validation),
            ("label = U&'low' UESCAPE 'é'", ErrorCode.Validation) })
        {
            await RefuseAsync(database, engine, SqlNullDistinctCases.Prefix + clause + SqlNullDistinctCases.Tail,
            code, image, position, identity);
        }
        await TokenBoundAsync(database, engine, image, position, identity);
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
        foreach (var clause in new[] { SqlNullDistinctCases.UnicodeClause, SqlNullDistinctCases.UnicodeCustomClause })
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

    private static async Task TokenBoundAsync(TestDatabase database, global::KeyLoad.Query.QueryEngine engine,
        string[] image, long position, byte[] identity)
    {
        var sql = SqlNullDistinctCases.Prefix + SqlNullDistinctCases.UnicodeClause
            + string.Concat(Enumerable.Repeat(EmptyFragment, database.Database.Limits.MaxQueryTokens))
            + SqlNullDistinctCases.Tail;
        await Assert.That(global::System.Text.Encoding.UTF8.GetByteCount(sql)
            <= database.Database.Limits.MaxQueryBytes).IsTrue();
        var request = new QueryRequest(database.Partition, sql, AllowFullScan: true);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(
            SqlNullOrderingProtocol.Reader, request)).Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
        await SqlNullDistinctPages.HealthyAsync(database.Database, database.Store, database.Partition,
            SqlNullDistinctRefusals.InitialRevision);
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
    }

    private const string EmptyFragment = "\n''";
    private const string Low = "low";
    private const string LowProjection = "{\"label\":\"low\"}";
    private const string BoundedFullScan = "bounded-full-scan";
}
