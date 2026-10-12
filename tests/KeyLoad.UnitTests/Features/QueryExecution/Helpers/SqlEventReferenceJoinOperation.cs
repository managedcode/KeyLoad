using KeyLoad.Query;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlEventReferenceJoinOperation
{
    internal static async Task VerifyAsync(TestDatabase database, bool repaired = false)
    {
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var before = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var request = SqlEventReferenceJoinFixture.Request(database.Partition);
        var page = engine.Execute("root", request);
        await SqlEventReferenceJoinAssertions.PageAsync(page, position, repaired: repaired);
        var ast = engine.ExecuteAst("root", SqlEventReferenceJoinFixture.Ast(database.Partition));
        await Assert.That(JsonDefaults.Serialize(ast)).IsEquivalentTo(JsonDefaults.Serialize(page), CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(before, CollectionOrdering.Matching);
    }

    internal static async Task RefusalsAsync(TestDatabase database)
    {
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var request = SqlEventReferenceJoinFixture.Request(database.Partition);
        await SqlEventReferenceJoinAssertions.RejectAsync(database,
            () => engine.Execute("root", request with { Parameters = null }), ErrorCode.Validation);
        await SqlEventReferenceJoinAssertions.RejectAsync(database,
            () => engine.Execute("root", request with { Sql = request.Sql.Replace("e.payload.score >= @minimum", "d.name = @minimum", StringComparison.Ordinal) }),
            ErrorCode.UnsupportedCapability);
        await SqlEventReferenceJoinAssertions.RejectAsync(database,
            () => engine.Execute("root", SqlEventReferenceJoinFixture.Request(database.Partition, SqlEventReferenceJoinFixture.InvalidStreamSet)),
            ErrorCode.Validation);
        var filteredInvalid = SqlEventReferenceJoinFixture.Request(database.Partition,
            SqlEventReferenceJoinFixture.InvalidStreamSet) with
        {
            Sql = SqlEventReferenceJoinFixture.Sql(SqlEventReferenceJoinFixture.InvalidStreamSet)
                .Replace("e.payload.score >= @minimum", "e.payload.score > 100", StringComparison.Ordinal),
            Parameters = null
        };
        await SqlEventReferenceJoinAssertions.RejectAsync(database,
            () => engine.Execute("root", filteredInvalid), ErrorCode.Validation);
        var invalidAst = SqlEventReferenceJoinFixture.Ast(database.Partition);
        await SqlEventReferenceJoinAssertions.RejectAsync(database,
            () => engine.ExecuteAst("root", invalidAst with
            {
                Query = invalidAst.Query with
                {
                    Collection = SqlEventReferenceJoinFixture.InvalidStreamSet,
                    Filter = new Comparison(new FieldOperand("/payload/score"), ">", new ValueOperand(System.Text.Json.JsonSerializer.SerializeToElement(100)))
                },
                Parameters = null
            }), ErrorCode.Validation);
        await SqlEventReferenceJoinAssertions.RejectAsync(database,
            () => engine.ExecuteAst("root", SqlEventReferenceJoinFixture.Ast(database.Partition) with
            {
                Query = SqlEventReferenceJoinFixture.Ast(database.Partition).Query with
                { InnerJoin = new("missingcustomers", "d", "/payload/to", "/entity_ref") }
            }), ErrorCode.NotFound);
        await SqlEventReferenceJoinAssertions.RejectAsync(database,
            () => engine.Execute("root", SqlEventReferenceJoinFixture.Request(database.Partition, generation: 2)),
            ErrorCode.TokenInvalidated);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var before = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() => engine.Execute("root", request, cancellationToken: cancellation.Token));
        await Assert.That(failure.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await VerifyAsync(database);
    }
}

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
        var record = NativeSerialization.Deserialize<EventRecord>(original);
        var damaged = new[]
        {
            record with { EventSequence = 0 },
            record with { Data = record.Data with { SchemaVersion = 0 } },
            record with { Data = record.Data with { EventType = "" } }
        };
        foreach (var fault in damaged)
        {
            await FaultAsync(database, key, original, NativeSerialization.Serialize(fault));
        }
        await SqlEventReferenceJoinCold.RunAsync(database, seed.Command, seed.Receipt,
            repair.Command, repair.Receipt);
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
            await QueriesAsync(database, expected, checked(position + 1));
        }
        catch (Exception error) when (global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        try
        {
            database.Store.Commit((transaction, _) => { transaction.Put(key, original); return true; });
            await UnchangedAsync(database, before, checked(position + 2));
            await Assert.That(JsonDefaults.Serialize(database.Store.Identity)).IsEquivalentTo(identity, CollectionOrdering.Matching);
            await SqlEventReferenceJoinOperation.VerifyAsync(database, repaired: true);
        }
        catch (Exception error) when (global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task QueriesAsync(TestDatabase database, string[] expected, long position)
    {
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var sql = SqlEventReferenceJoinFixture.Request(database.Partition);
        var ast = SqlEventReferenceJoinFixture.Ast(database.Partition);
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
            await UnchangedAsync(database, expected, position);
            await Assert.That(JsonDefaults.Serialize(database.Store.Identity)).IsEquivalentTo(identity, CollectionOrdering.Matching);
        }
    }

    private static async Task UnchangedAsync(TestDatabase database, string[] expected, long position)
    {
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }
}
