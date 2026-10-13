using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlOffsetPages
{
    internal static async Task HealthyAsync(global::KeyLoad.Core.DatabaseEngine owner,
        global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store, PartitionRef partition, string principal)
    {
        var engine = new QueryEngine(owner, UnitExecutionOptions.QueryExecution());
        await ManifestAsync(engine, owner.Limits);
        var sql = SqlOffsetProtocol.ScalarRequest(partition);
        var ast = SqlOffsetProtocol.Scalar(partition);
        var sqlPage = engine.Execute(principal, sql);
        var astPage = engine.ExecuteAst(principal, ast);
        await ScalarAsync(sqlPage, "b", """{"title":"second"}""", store.Position, final: false);
        await ScalarAsync(astPage, "b", """{"title":"second"}""", store.Position, final: false);
        sql = sql with { Cursor = sqlPage.Cursor };
        ast = ast with { Cursor = astPage.Cursor };
        sqlPage = engine.Execute(principal, sql);
        astPage = engine.ExecuteAst(principal, ast);
        await ScalarAsync(sqlPage, "c", """{"title":"third"}""", store.Position, final: false);
        await ScalarAsync(astPage, "c", """{"title":"third"}""", store.Position, final: false);
        sqlPage = engine.Execute(principal, sql with { Cursor = sqlPage.Cursor });
        astPage = engine.ExecuteAst(principal, ast with { Cursor = astPage.Cursor });
        await ScalarAsync(sqlPage, "d", """{"title":"fourth"}""", store.Position, final: true);
        await ScalarAsync(astPage, "d", """{"title":"fourth"}""", store.Position, final: true);
        var expected = JoinPage(store.Position);
        await ExactAsync(engine.Execute(principal, SqlOffsetProtocol.JoinRequest(partition)), expected);
        await ExactAsync(engine.ExecuteAst(principal, SqlOffsetProtocol.Join(partition)), expected);
        var alternate = SqlOffsetProtocol.JoinRequest(partition) with
        { Sql = SqlOffsetProtocol.JoinSql.Replace("LIMIT 1 OFFSET 1", "OFFSET 1 ROWS LIMIT 1", StringComparison.Ordinal) };
        await ExactAsync(engine.Execute(principal, alternate), expected);
    }
    internal static QueryPage JoinPage(long cut) => new(
        [new("b", SqlOffsetProtocol.InitialRevision, """{"title":"second","name":"Bee"}""", false, [],
            [new(SqlOffsetProtocol.LeftAlias, "b", SqlOffsetProtocol.InitialRevision),
             new(SqlOffsetProtocol.RightAlias, "c2", SqlOffsetProtocol.InitialRevision)])],
        null, cut, "bounded-primary-key-inner-join");
    private static async Task ScalarAsync(QueryPage page, string id, string json, long cut, bool final)
    {
        if (final)
        { await Assert.That(page.Cursor).IsNull(); }
        else
        { await Assert.That(page.Cursor).IsNotNull(); }
        // The opaque signed cursor is checked by independent next-row/cut continuation, not a fabricated signature.
        await ExactAsync(page, new([new(id, SqlOffsetProtocol.InitialRevision, json, false, [])],
            page.Cursor, cut, "bounded-full-scan"));
    }
    internal static async Task ExactAsync(QueryPage actual, QueryPage expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    private static async Task ManifestAsync(QueryEngine engine, DatabaseLimits limits)
    {
        var expected = new QueryCapabilityManifest(1, 1, "Q1", "atomicPartition", "decimal", "distinctFromNull",
            ["SQL", "JSON", "C#"], ["comparison", "AND", "OR", "NOT", "IN", "BETWEEN", "NOT BETWEEN", "IS NULL", "IS MISSING",
                "IS DISTINCT FROM", "IS NOT DISTINCT FROM"],
            limits.MaxResults, limits.MaxScanRecords, limits.MaxQueryBytes, limits.MaxQueryDepth, true, true,
            limits.MaxQueryReadBytes, ["Q1", "documentChangeFeed", "scalarLiveQuery", "modelViewsV1", "graph-search-v1", "graph-path-v1",
                "Q2.InnerJoin.v1", "Q2.InnerJoin.LeftPredicate.v1", "Q2.QueueEntityRefJoin.v1", "Q2.EventEntityRefJoin.v1",
                "Q1.Offset.v1", "Q2.InnerJoin.Offset.v1", "Q1.NullOrdering.v1", "SQL.FetchCount.v1", "SQL.NullDistinct.v1"], [1, 2], [1, 2]);
        await Assert.That(JsonDefaults.Serialize(engine.Capabilities).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
    internal static async Task ReplayAsync(global::KeyLoad.Core.DatabaseEngine owner, CommandRequest seed, CommitReceipt receipt)
    {
        var actual = owner.ApplyEmbedded(new(seed.CommandId, OperationKind.Batch, SqlOffsetProtocol.Root, default,
            global::System.Text.Json.JsonSerializer.Serialize(seed, JsonDefaults.Options)), cancellationToken: default).Get<CommitReceipt>();
        await Assert.That(NativeSerialization.Serialize(actual).AsSpan().SequenceEqual(NativeSerialization.Serialize(receipt))).IsTrue();
    }
}
