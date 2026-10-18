using KeyLoad.Query;
namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlNullOrderingPages
{
    internal static async Task HealthyAsync(global::KeyLoad.Core.DatabaseEngine database,
        global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store, PartitionRef partition, string principal)
    {
        var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        await Assert.That(engine.Capabilities.ReadProfiles.Contains(SqlNullOrderingProtocol.Profile)).IsTrue();
        await Assert.That(engine.Capabilities.ReadProfiles.Contains(SqlNullOrderingProtocol.UsingProfile)).IsFalse();
        var image = SqlInnerJoinRetainedIdentityFlow.Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        foreach (var descending in new[] { false, true })
        {
            foreach (var first in new[] { false, true })
            {
                var expected = Expected(descending, first, position);
                await EqualAsync(expected, engine.ExecuteAst(principal, SqlNullOrderingProtocol.Ast(partition, descending, first)));
                await ContinuationAsync(engine, partition, principal, descending, first, position);
                if (first)
                {
                    await NativeContinuationAsync(engine, partition, principal, descending, position);
                }
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
        var second = engine.ExecuteAst(principal, request with
        {
            Cursor = initial.Cursor
        });
        await Assert.That(second.Cursor).IsNotNull();
        await EqualAsync(new([.. full.Rows.Skip(PageRows).Take(PageRows)], second.Cursor, cut, full.AccessPath), second);
        var final = engine.ExecuteAst(principal, request with
        {
            Cursor = second.Cursor
        });
        await EqualAsync(new([.. full.Rows.Skip(PageRows + PageRows)], null, cut, full.AccessPath), final);
    }

    private static async Task NativeContinuationAsync(QueryEngine engine, PartitionRef partition,
        string principal, bool descending, long cut)
    {
        string[] ids = descending ? ["high", "low"] : ["low", "high"];
        var full = new QueryPage([.. ids.Select(id => new QueryRow(id, 1,
            "{\"label\":\"" + id + "\"}", RedactedFields: []))], null, cut, "bounded-full-scan");
        await EqualAsync(full, engine.Execute(principal, SqlNullOrderingProtocol.Native(partition, descending)));
        await EqualAsync(full, engine.ExecuteAst(principal, SqlNullOrderingProtocol.NativeAst(partition, descending)));
        var sql = SqlNullOrderingProtocol.Native(partition, descending, NativePageRows);
        var initial = engine.Execute(principal, sql);
        await Assert.That(initial.Cursor).IsNotNull();
        await EqualAsync(new([full.Rows[FirstRow]], initial.Cursor, cut, full.AccessPath), initial);
        var second = engine.ExecuteAst(principal,
            SqlNullOrderingProtocol.NativeAst(partition, descending, NativePageRows) with
            {
                Cursor = initial.Cursor
            });
        await EqualAsync(new([full.Rows[NativePageRows]], null, cut, full.AccessPath), second);
    }

    private const int FirstRow = 0;
    private const int NativePageRows = 1;

    private const int PageRows = 2;

    internal static async Task EqualAsync(QueryPage expected, QueryPage actual)
    {
        var actualBytes = JsonDefaults.Serialize(actual);
        var expectedBytes = JsonDefaults.Serialize(expected);
        await Assert.That(global::System.Text.Encoding.UTF8.GetString(actualBytes))
            .IsEqualTo(global::System.Text.Encoding.UTF8.GetString(expectedBytes));
        await Assert.That(actualBytes).IsEquivalentTo(expectedBytes,
            global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    internal static async Task ReplayAsync(global::KeyLoad.Core.DatabaseEngine database, CommandRequest seed, CommitReceipt receipt)
    {
        var operation = database.CreateCommand(OperationKind.Batch, seed.CommandId, SqlNullOrderingProtocol.Root, default, global::System.Text.Json.JsonSerializer.Serialize(seed, JsonDefaults.Options));
        var actual = database.ApplyEmbedded(operation, cancellationToken: default).Get<CommitReceipt>();
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
    }
}
