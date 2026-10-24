namespace KeyLoad.UnitTests.QueryExecution;

internal static class SqlNumericSeparatorControls
{
    internal static async Task InvalidAsync(TestDatabase database, global::KeyLoad.Query.QueryEngine engine)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        foreach (var clause in new[] { "score = 1_",
            "score = 1__0",
            "score = .1_",
            "score = 1_.0",
            "score = 1._0",
            "score = 1e_1",
            "score = 1_e1",
            "score = 1e+_1",
            "score = 0x__1",
            "score = 0x1_",
            "score = 0b_",
            "score = 0o1__0",
            "score = + 0b_1",
            "score = 79_228_162_514_264_337_593_543_950_336",
            "score = 0x_1_0000_0000_0000_0000_0000_0000",
            "score = 0o_1_0000_0000_0000_0000_0000_0000_0000_0000" })
        {
            await RefuseAsync(database, engine, SqlNullDistinctCases.Prefix + clause + SqlNullDistinctCases.Tail,
            ErrorCode.Validation, image, position, identity);
        }
        foreach (var window in new[] { "LIMIT 0_0",
            "LIMIT 2_147_483_648",
            "OFFSET 2_147_483_648 LIMIT 1",
            "FETCH FIRST 2_147_483_648 ROWS ONLY",
            "LIMIT +0_1",
            "OFFSET -0_1 LIMIT 1",
            "FETCH FIRST 0x_1 ROW ONLY",
            "LIMIT 0b_1",
            "OFFSET 0o_1 LIMIT 1",
            "LIMIT 1_0e-1",
            "FETCH FIRST 1.0_0 ROW ONLY" })
        {
            await RefuseAsync(database, engine, SqlNullDistinctCases.Prefix + SqlNullDistinctCases.NumericSeparatorClause
            + OrderBy + window, ErrorCode.Validation, image, position, identity);
        }
        foreach (var window in new[] { "LIMIT 0_1 OFFSET 0_0 LIMIT 1", "FETCH FIRST 0_1 ROW ONLY LIMIT 1" })
        {
            await RefuseAsync(database, engine, SqlNullDistinctCases.Prefix + SqlNullDistinctCases.NumericSeparatorClause
            + OrderBy + window, ErrorCode.Validation, image, position, identity);
        }
        await CancelledAsync(database, engine, image, position, identity);
    }

    private static async Task RefuseAsync(TestDatabase database, global::KeyLoad.Query.QueryEngine engine,
        string sql, ErrorCode code, string[] image, long position, byte[] identity)
    {
        var error = global::TUnit.Assertions.Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(
            SqlNullOrderingProtocol.Reader, new(database.Partition, sql, AllowFullScan: true)));
        await global::TUnit.Assertions.Assert.That(error.Code).IsEqualTo(code);
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
        foreach (var clause in new[] { SqlNullDistinctCases.NumericSeparatorClause,
            SqlNullDistinctCases.NumericRadixSeparatorClause, SqlNullDistinctCases.NumericOctalSeparatorClause })
        {
            var request = new QueryRequest(database.Partition, SqlNullDistinctCases.Prefix + clause
                + SqlNullDistinctCases.Tail, AllowFullScan: true);
            var error = global::TUnit.Assertions.Assert.ThrowsExactly<OperationCanceledException>(() => engine.Execute(
                SqlNullOrderingProtocol.Reader, request, cancellationToken: originalToken));
            await global::TUnit.Assertions.Assert.That(error.CancellationToken).IsEqualTo(originalToken);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
            using var healthy = new CancellationTokenSource();
            await global::TUnit.Assertions.Assert.That(healthy.Token.IsCancellationRequested).IsFalse();
            await SqlNullOrderingPages.EqualAsync(Page(position, SqlNullDistinctRefusals.InitialRevision),
                engine.Execute(SqlNullOrderingProtocol.Reader, request, cancellationToken: healthy.Token));
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
        }
    }

    internal static async Task ParametersAsync(global::KeyLoad.Query.QueryEngine engine,
        global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store, PartitionRef partition, long lowRevision)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        var parameters = new Dictionary<string, global::System.Text.Json.JsonElement>
        {
            [Argument] = global::System.Text.Json.JsonSerializer.SerializeToElement(UnitValue)
        };
        var item = new SqlNullDistinctCase(ParameterClause, new global::KeyLoad.Query.Logical(
            new global::KeyLoad.Query.Comparison(SqlNullDistinctCases.Score, "=", global::KeyLoad.Query.ValueOperand.Create(UnitValue)), "AND",
            new global::KeyLoad.Query.Comparison(new global::KeyLoad.Query.ParameterOperand(Argument), "=", global::KeyLoad.Query.ValueOperand.Create(UnitValue))), [Low]);
        await SqlNullOrderingPages.EqualAsync(Page(position, lowRevision), engine.Execute(SqlNullOrderingProtocol.Reader,
            new(partition, SqlNullDistinctCases.Prefix + ParameterClause + SqlNullDistinctCases.Tail, parameters, AllowFullScan: true)));
        await SqlNullOrderingPages.EqualAsync(Page(position, lowRevision), engine.ExecuteAst(SqlNullOrderingProtocol.Reader,
            SqlNullDistinctCases.Ast(partition, item) with
            {
                Parameters = parameters
            }));
        await SqlNullOrderingRefusals.UnchangedAsync(store, image, position, identity);
        await WindowsAsync(engine, store, partition, lowRevision);
    }

    private static async Task WindowsAsync(global::KeyLoad.Query.QueryEngine engine,
        global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store, PartitionRef partition, long lowRevision)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        foreach (var window in new[] { "OFFSET 0 ROWS FETCH FIRST 5 ROWS ONLY", "OFFSET 0 ROWS FETCH NEXT 5 ROWS ONLY" })
        {
            var request = new QueryRequest(partition, SqlNullDistinctCases.WindowPrefix + SqlNullDistinctCases.NumericSeparatorClause
                + OrderBy + window, AllowFullScan: true);
            await SqlNullOrderingPages.EqualAsync(Page(position, lowRevision), engine.Execute(SqlNullOrderingProtocol.Reader, request));
            await SqlNullOrderingRefusals.UnchangedAsync(store, image, position, identity);
        }
    }

    private static global::KeyLoad.QueryPage Page(long position, long revision)
        => new([new global::KeyLoad.QueryRow(Low, revision, LowProjection, RedactedFields: [])], null, position, FullScan);

    private const string Argument = "argument";
    private const string Low = "low";
    private const string LowProjection = "{\"label\":\"low\"}";
    private const string OrderBy = " ORDER BY label ASC ";
    private const string FullScan = "bounded-full-scan";
    private const string ParameterClause = "score = 1 AND @argument = 1";
    private const decimal UnitValue = 1m;
}
