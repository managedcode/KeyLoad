using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class SqlImplicitAliasPages
{
    internal const string BasicSql = "SELECT TOP (5) score projected_score FROM null_ordering WHERE score IS NULL ORDER BY label ASC";
    private const string Prefix = "SELECT TOP (5) score ";
    private const string Tail = " FROM null_ordering WHERE score IS NULL ORDER BY label ASC";
    private static readonly (string Label, string Name, string Json)[] Cases =
    [
        ("projected_score", "projected_score", "{\"projected_score\":null}"),
        ("\"ORDER\"", "ORDER", "{\"ORDER\":null}"),
        ("\"FROM\"", "FROM", "{\"FROM\":null}"),
        ("'copied'", "copied", "{\"copied\":null}")
    ];

    internal static async Task HealthyAsync(QueryEngine engine, PartitionRef partition, string principal, long cut)
    {
        foreach (var (label, name, json) in Cases)
        {
            var expected = new QueryPage(
                [new("null_a", Revision, json, RedactedFields: []), new("null_b", Revision, json, RedactedFields: [])],
                null, cut, AccessPath);
            await SqlNullOrderingPages.EqualAsync(expected, engine.Execute(principal,
                new(partition, Prefix + label + Tail, AllowFullScan: true)));
            await SqlNullOrderingPages.EqualAsync(expected, engine.Execute(principal,
                new(partition, Prefix + ExplicitAs + label + Tail, AllowFullScan: true)));
            await SqlNullOrderingPages.EqualAsync(expected, engine.ExecuteAst(principal, Ast(partition, name)));
        }
    }

    private static AstQueryRequest Ast(PartitionRef partition, string name)
        => new(partition, new(SqlNullOrderingProtocol.Collection, null, [new(ScorePath, name)],
            new NullTest(new FieldOperand(ScorePath), false, false), [new(LabelPath, false)],
            SqlNullOrderingProtocol.ResultCount), AllowFullScan: true);

    internal static async Task InvalidAsync(TestDatabase database, QueryEngine engine)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var cut = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        foreach (var sql in new[] { "SELECT TOP (5) score copied,label copied FROM null_ordering",
            "SELECT TOP (5) score ORDER FROM null_ordering", "SELECT TOP (5) score AS FROM null_ordering" })
        {
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlNullOrderingProtocol.Root,
                new(database.Partition, sql, AllowFullScan: true))).Code).IsEqualTo(ErrorCode.Validation);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, cut, identity);
            await HealthyAsync(engine, database.Partition, SqlNullOrderingProtocol.Root, cut);
        }
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() => engine.Execute(SqlNullOrderingProtocol.Root,
            new(database.Partition, BasicSql, AllowFullScan: true), cancellationToken: cancelled.Token));
        await Assert.That(failure.CancellationToken).IsEqualTo(cancelled.Token);
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, cut, identity);
        await HealthyAsync(engine, database.Partition, SqlNullOrderingProtocol.Root, cut);
    }

    private const string ExplicitAs = "AS ";
    private const string ScorePath = "/score";
    private const string LabelPath = "/label";
    private const string AccessPath = "bounded-full-scan";
    private const long Revision = 1;
}
