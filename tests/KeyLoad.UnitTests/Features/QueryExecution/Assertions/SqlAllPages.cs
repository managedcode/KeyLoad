using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlAllPages
{
    internal const string Keyword = "SELECT ALL ";
    internal const string DefaultKeyword = "SELECT ";
    internal const string DuplicateSql = "SELECT ALL score AS score FROM null_ordering WHERE score IS NULL ORDER BY label ASC LIMIT 5";
    internal static string Explicit(string sql) => Keyword + sql[DefaultKeyword.Length..];

    internal static async Task DuplicatesAsync(QueryEngine engine, PartitionRef partition, string principal, long cut)
    {
        var expected = new QueryPage(
            [new("null_a", Revision, "{\"score\":null}", RedactedFields: []),
             new("null_b", Revision, "{\"score\":null}", RedactedFields: [])],
            null, cut, AccessPath);
        await SqlNullOrderingPages.EqualAsync(expected, engine.Execute(principal,
            new(partition, DuplicateSql, AllowFullScan: true)));
        await SqlNullOrderingPages.EqualAsync(expected, engine.Execute(principal,
            new(partition, DuplicateSql.Replace(Keyword, DefaultKeyword, StringComparison.Ordinal), AllowFullScan: true)));
        await SqlNullOrderingPages.EqualAsync(expected, engine.ExecuteAst(principal, Ast(partition)));
    }

    internal static AstQueryRequest Ast(PartitionRef partition)
        => new(partition, new(SqlNullOrderingProtocol.Collection, null, [new(ScorePath, ScoreName)],
            new NullTest(new FieldOperand(ScorePath), false, false), [new(LabelPath, false)],
            SqlNullOrderingProtocol.ResultCount), AllowFullScan: true);

    internal static async Task InvalidAsync(TestDatabase database, QueryEngine engine)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var cut = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        foreach (var sql in new[] { "SELECT ALL FROM null_ordering LIMIT 5",
            "SELECT ALL ALL label FROM null_ordering LIMIT 5" })
        {
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlNullOrderingProtocol.Root,
                new(database.Partition, sql, AllowFullScan: true))).Code).IsEqualTo(ErrorCode.Validation);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, cut, identity);
            await DuplicatesAsync(engine, database.Partition, SqlNullOrderingProtocol.Root, cut);
        }
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() => engine.Execute(SqlNullOrderingProtocol.Root,
            new(database.Partition, DuplicateSql, AllowFullScan: true), cancellationToken: cancelled.Token));
        await Assert.That(failure.CancellationToken).IsEqualTo(cancelled.Token);
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, cut, identity);
        await DuplicatesAsync(engine, database.Partition, SqlNullOrderingProtocol.Root, cut);
    }

    private const string ScorePath = "/score";
    private const string LabelPath = "/label";
    private const string ScoreName = "score";
    private const string AccessPath = "bounded-full-scan";
    private const long Revision = 1;
}
