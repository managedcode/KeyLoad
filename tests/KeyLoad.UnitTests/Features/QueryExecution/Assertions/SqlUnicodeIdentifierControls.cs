namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlUnicodeIdentifierControls
{
    internal static async Task InvalidAsync(TestDatabase database, global::KeyLoad.Query.QueryEngine engine)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        foreach (var (clause, code) in new[]
        { ("U&\"score\\D800\" = 1", ErrorCode.UnsupportedCapability),
            ("U&\"score\\DC00\" = 1", ErrorCode.UnsupportedCapability),
            ("U&\"score\\0000\" = 1", ErrorCode.UnsupportedCapability),
            ("U&\"score\\+110000\" = 1", ErrorCode.UnsupportedCapability),
            ("U&\"score\\zzzz\" = 1", ErrorCode.UnsupportedCapability),
            ("U&\"score", ErrorCode.Validation),
            ("U&\"score\" UESCAPE '0' = 1", ErrorCode.Validation),
            ("U&\"score\" UESCAPE '!!' = 1", ErrorCode.Validation),
            ("U&\"score\" UESCAPE 'é' = 1", ErrorCode.Validation) })
        {
            await RefuseAsync(database, engine, SqlNullDistinctCases.Prefix + clause + SqlNullDistinctCases.Tail,
            code, image, position, identity);
        }
        foreach (var (window, code) in new[]
        { ("OFFSET \"1\" ROWS FETCH NEXT 1 ROWS ONLY", ErrorCode.UnsupportedCapability),
            ("OFFSET 0 ROWS FETCH NEXT \"1\" ROW ONLY", ErrorCode.UnsupportedCapability),
            ("OFFSET 0 ROWS FETCH NEXT N'1' ROW ONLY", ErrorCode.Validation) })
        {
            await RefuseAsync(database, engine, SqlNullDistinctCases.WindowPrefix + SqlUnicodeIdentifierCases.Clause
            + OrderBy + window, code, image, position, identity);
        }

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
            new(partition, ProjectionPrefix + ParameterClause + SqlNullDistinctCases.Tail, parameters, AllowFullScan: true)));
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
            var request = new QueryRequest(partition, SqlNullDistinctCases.WindowPrefix + SqlUnicodeIdentifierCases.Clause
                + OrderBy + window, AllowFullScan: true);
            await SqlNullOrderingPages.EqualAsync(Page(position, lowRevision), engine.Execute(SqlNullOrderingProtocol.Reader, request));
            await SqlNullOrderingRefusals.UnchangedAsync(store, image, position, identity);
        }
    }

    private static global::KeyLoad.QueryPage Page(long position, long revision)
        => new([new global::KeyLoad.QueryRow(Low, revision, LowProjection, RedactedFields: [])], null, position, FullScan);

    private const string ProjectionPrefix = "SELECT TOP (5) \"label\" AS \"label\" FROM \"null_ordering\" WHERE ";
    private const string Argument = "argument";
    private const string Low = "low";
    private const string LowProjection = "{\"label\":\"low\"}";
    private const string OrderBy = " ORDER BY label ASC ";
    private const string FullScan = "bounded-full-scan";
    private const string ParameterClause = "\"score\" = 1 AND @argument = 1";
    private const decimal UnitValue = 1m;
}
