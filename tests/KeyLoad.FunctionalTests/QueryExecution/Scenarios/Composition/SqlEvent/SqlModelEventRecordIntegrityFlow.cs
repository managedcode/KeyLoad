using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class SqlModelEventRecordIntegrityFlow
{
    internal static async Task RunAsync(TestDatabase database)
    {
        var seed = SqlEventReferenceJoinFixture.Seed(database);
        var repair = await SqlEventReferenceJoinPolicy.RunAsync(database);
        var key = Core.KeySpace.Partition("event", database.Partition,
            SqlEventReferenceJoinFixture.StreamSet, SqlEventReferenceJoinFixture.Stream, 1L, 5L);
        var original = database.Store.Read(view => view.ReadOwnedValue(key))
                       ?? throw new InvalidOperationException("The original retained event is absent.");
        var record = OrleansSerialization.Deserialize<EventRecord>(original);
        var damaged = new[]
        {
            record with { EventSequence = 0 },
            record with { Data = record.Data with { SchemaVersion = 0 } },
            record with { Data = record.Data with { EventType = "" } }
        };
        foreach (var fault in damaged)
        {
            await FaultAsync(database, key, original, OrleansSerialization.Serialize(fault));
        }
        await SqlEventReferenceJoinCold.RunAsync(database, seed.Command, seed.Receipt,
            repair.Command, repair.Receipt);
        await SqlModelEventRecordIntegrityCold.RunAsync(database, key, original, damaged);
    }

    private static async Task FaultAsync(TestDatabase database, byte[] key, byte[] original, byte[] damaged)
    {
        var before = QueueWholeFlowStorage.Bytes(database.Store);
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        var position = database.Store.Position;
        var prefix = Convert.ToHexString(key) + ":";
        var originalRow = prefix + Convert.ToHexString(original);
        await Assert.That(before.Single(row => row.StartsWith(prefix, StringComparison.Ordinal))).IsEqualTo(originalRow);
        var expected = before.Select(row => row == originalRow ? prefix + Convert.ToHexString(damaged) : row).ToArray();
        var failures = new List<Exception>();
        try
        {
            database.Store.Commit((transaction, _) => { transaction.Put(key, damaged); return true; });
            await UnchangedAsync(database, expected, checked(position + 1));
            await Assert.That(JsonDefaults.Serialize(database.Store.Identity)).IsEquivalentTo(identity, CollectionOrdering.Matching);
            await QueriesAsync(database.Store, database.Database, database.Partition, expected, checked(position + 1));
        }
        catch (Exception error) when (global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        try
        {
            database.Store.Commit((transaction, _) => { transaction.Put(key, original); return true; });
            await UnchangedAsync(database, before, checked(position + 2));
            await Assert.That(JsonDefaults.Serialize(database.Store.Identity)).IsEquivalentTo(identity, CollectionOrdering.Matching);
            await SqlEventReferenceJoinOperation.VerifyAsync(database, repaired: true);
        }
        catch (Exception error) when (global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task QueriesAsync(global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store,
        Core.DatabaseEngine database, PartitionRef partition, string[] expected, long position)
    {
        var identity = JsonDefaults.Serialize(store.Identity);
        var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        var sql = SqlEventReferenceJoinFixture.Request(partition);
        var ast = SqlEventReferenceJoinFixture.Ast(partition);
        var falseFilter = new Comparison(new FieldOperand("/payload/score"), ">",
            new ValueOperand(global::System.Text.Json.JsonSerializer.SerializeToElement(100)));
        var operations = new Action[]
        {
            () => engine.Execute("root", sql),
            () => engine.Execute("root", sql with
                { Sql = sql.Sql.Replace("e.payload.score >= @minimum", "e.payload.score > 100", StringComparison.Ordinal), Parameters = null }),
            () => engine.Execute("root", sql with { Sql = sql.Sql.Replace("LIMIT 10", "LIMIT 1", StringComparison.Ordinal) }),
            () => engine.ExecuteAst("root", ast),
            () => engine.ExecuteAst("root", ast with { Query = ast.Query with { Filter = falseFilter }, Parameters = null }),
            () => engine.ExecuteAst("root", ast with { Query = ast.Query with { Limit = 1 } })
        };
        foreach (var operation in operations)
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(operation);
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(failure.Message).IsEqualTo("The retained event source is inconsistent.");
            await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(expected, CollectionOrdering.Matching);
            await Assert.That(store.Position).IsEqualTo(position);
            await Assert.That(JsonDefaults.Serialize(store.Identity)).IsEquivalentTo(identity, CollectionOrdering.Matching);
        }
    }

    private static async Task UnchangedAsync(TestDatabase database, string[] expected, long position)
    {
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }
}
