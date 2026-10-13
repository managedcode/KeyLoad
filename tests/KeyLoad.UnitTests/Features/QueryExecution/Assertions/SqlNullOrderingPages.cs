using KeyLoad.Query;
namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlNullOrderingPages
{
    internal static async Task HealthyAsync(global::KeyLoad.Core.DatabaseEngine database,
        global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store, PartitionRef partition, string principal)
    {
        var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        await Assert.That(engine.Capabilities.ReadProfiles.Contains(SqlNullOrderingProtocol.Profile)).IsTrue();
        await Assert.That(engine.Capabilities.ReadProfiles.Contains(SqlNullOrderingProtocol.UsingProfile)).IsTrue();
        var image = SqlInnerJoinRetainedIdentityFlow.Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        foreach (var descending in new[] { false, true })
        {
            foreach (var first in new[] { false, true })
            {
                var expected = Expected(descending, first, position);
                await EqualAsync(expected, engine.Execute(principal, SqlNullOrderingProtocol.Sql(partition, descending, first)));
                await EqualAsync(expected, engine.Execute(principal, SqlNullOrderingProtocol.Using(partition, descending, first)));
                var all = SqlNullOrderingProtocol.Sql(partition, descending, first);
                await EqualAsync(expected, engine.Execute(principal, all with { Sql = SqlAllPages.Explicit(all.Sql) }));
                await EqualAsync(expected, engine.ExecuteAst(principal, SqlNullOrderingProtocol.Ast(partition, descending, first)));
                await ContinuationAsync(engine, partition, principal, descending, first, position);
                await UsingContinuationAsync(engine, partition, principal, descending, first, position);
                await SqlNullOrderingRefusals.UnchangedAsync(store, image, position, identity);
            }
        }
        await SqlAllPages.DuplicatesAsync(engine, partition, principal, position);
        await SqlNullOrderingRefusals.UnchangedAsync(store, image, position, identity);
        await SqlImplicitAliasPages.HealthyAsync(engine, partition, principal, position);
        await SqlNullOrderingRefusals.UnchangedAsync(store, image, position, identity);
    }

    internal static QueryPage Expected(bool descending, bool first, long cut)
    {
        string[] nonnull = descending ? ["high", "low", "missing"] : ["missing", "low", "high"];
        string[] ids = first ? ["null_a", "null_b", .. nonnull] : [.. nonnull, "null_a", "null_b"];
        return new([.. ids.Select(id => new QueryRow(id, 1, "{\"label\":\"" + id + "\"}", RedactedFields: []))],
            null, cut, "bounded-full-scan");
    }

    private static async Task ContinuationAsync(QueryEngine engine, PartitionRef partition,
        string principal, bool descending, bool first, long cut)
    {
        var full = Expected(descending, first, cut);
        var request = SqlNullOrderingProtocol.Ast(partition, descending, first, PageRows);
        var initial = engine.ExecuteAst(principal, request);
        await Assert.That(initial.Cursor).IsNotNull();
        await EqualAsync(new([.. full.Rows.Take(PageRows)], initial.Cursor, cut, full.AccessPath), initial);
        var second = engine.ExecuteAst(principal, request with { Cursor = initial.Cursor });
        await Assert.That(second.Cursor).IsNotNull();
        await EqualAsync(new([.. full.Rows.Skip(PageRows).Take(PageRows)], second.Cursor, cut, full.AccessPath), second);
        var final = engine.ExecuteAst(principal, request with { Cursor = second.Cursor });
        await EqualAsync(new([.. full.Rows.Skip(PageRows + PageRows)], null, cut, full.AccessPath), final);
    }

    private static async Task UsingContinuationAsync(QueryEngine engine, PartitionRef partition,
        string principal, bool descending, bool first, long cut)
    {
        var full = Expected(descending, first, cut);
        var sql = SqlNullOrderingProtocol.Using(partition, descending, first, PageRows);
        var ast = SqlNullOrderingProtocol.Ast(partition, descending, first, PageRows);
        var initial = engine.Execute(principal, sql);
        await Assert.That(initial.Cursor).IsNotNull();
        await EqualAsync(new([.. full.Rows.Take(PageRows)], initial.Cursor, cut, full.AccessPath), initial);
        var second = engine.ExecuteAst(principal, ast with { Cursor = initial.Cursor });
        await Assert.That(second.Cursor).IsNotNull();
        await EqualAsync(new([.. full.Rows.Skip(PageRows).Take(PageRows)], second.Cursor, cut, full.AccessPath), second);
        var final = engine.Execute(principal,
            SqlNullOrderingProtocol.Sql(partition, descending, first, PageRows) with { Cursor = second.Cursor });
        await EqualAsync(new([.. full.Rows.Skip(PageRows + PageRows)], null, cut, full.AccessPath), final);
    }

    private const int PageRows = 2;

    internal static async Task EqualAsync(QueryPage expected, QueryPage actual)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();

    internal static async Task ReplayAsync(global::KeyLoad.Core.DatabaseEngine database, CommandRequest seed, CommitReceipt receipt)
    {
        var operation = new ReplicatedOperation(seed.CommandId, OperationKind.Batch, SqlNullOrderingProtocol.Root,
            default, global::System.Text.Json.JsonSerializer.Serialize(seed, JsonDefaults.Options));
        var actual = database.ApplyEmbedded(operation, cancellationToken: default).Get<CommitReceipt>();
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
    }
}
