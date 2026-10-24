using KeyLoad.Query;
using KeyLoad.UnitTests.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class SqlEventReferenceJoinBudget
{
    private const int TenObservedRecords = 10;

    internal static async Task RunAsync(TestDatabase database)
    {
        var engine = Engine(database);
        var request = SqlEventReferenceJoinFixture.Request(database.Partition);
        await SqlEventReferenceJoinAssertions.RejectAsync(database,
            () => engine.Execute("root", request), ErrorCode.BudgetExceeded);
        var falseFilter = request with
        {
            Sql = request.Sql.Replace("e.payload.score >= @minimum", "e.payload.score > 100", StringComparison.Ordinal),
            Parameters = null
        };
        await SqlEventReferenceJoinAssertions.RejectAsync(database,
            () => engine.Execute("root", falseFilter), ErrorCode.BudgetExceeded);
        await SqlEventReferenceJoinAssertions.RejectAsync(database,
            () => engine.Execute("root", request with { Sql = request.Sql.Replace("TOP (10)", "TOP (1)", StringComparison.Ordinal) }),
            ErrorCode.BudgetExceeded);
        await BytesAsync(database);
        await SqlEventReferenceJoinOperation.VerifyAsync(database);
    }

    private static QueryEngine Engine(TestDatabase database)
        => Engine(database, database.Database.Limits with
        {
            MaxScanRecords = TenObservedRecords
        });

    private static QueryEngine Engine(TestDatabase database, DatabaseLimits limits,
        global::KeyLoad.Query.QueryExecutionOptions? execution = null)
    {
        var owner = new DatabaseEngine(database.Store, database.Database.Authorization,
            UnitExecutionOptions.DatabaseLimits(limits), UnitExecutionOptions.DueWork(),
            UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(),
            UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.RuntimeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution());
        return new(owner, UnitExecutionOptions.QueryExecution(execution));
    }
    private static async Task BytesAsync(TestDatabase database)
    {
        var before = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        await ResultBytesAsync(database, position);
        await ReadBytesAsync(database, position);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(before, CollectionOrdering.Matching);
    }

    private static async Task ResultBytesAsync(TestDatabase database, long position)
    {
        var request = SqlEventReferenceJoinFixture.Request(database.Partition);
        var normal = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution()).Execute("root", request);
        await SqlEventReferenceJoinAssertions.PageAsync(normal, position);
        var observedBytes = JsonDefaults.Serialize(normal).Length;
        await Assert.That(observedBytes).IsGreaterThan(1);
        var exact = Engine(database, database.Database.Limits,
            new global::KeyLoad.Query.QueryExecutionOptions { MaximumResultBytes = observedBytes });
        await SqlEventReferenceJoinAssertions.PageAsync(exact.Execute("root", request), position);
        await SqlEventReferenceJoinAssertions.PageAsync(exact.ExecuteAst("root",
            SqlEventReferenceJoinFixture.Ast(database.Partition)), position);
        var oneLess = Engine(database, database.Database.Limits,
            new global::KeyLoad.Query.QueryExecutionOptions { MaximumResultBytes = observedBytes - 1 });
        await SqlEventReferenceJoinAssertions.RejectAsync(database,
            () => oneLess.Execute("root", request), ErrorCode.BudgetExceeded);
        await SqlEventReferenceJoinAssertions.RejectAsync(database,
            () => oneLess.ExecuteAst("root", SqlEventReferenceJoinFixture.Ast(database.Partition)), ErrorCode.BudgetExceeded);
    }

    private static async Task ReadBytesAsync(TestDatabase database, long position)
    {
        var ast = SqlEventReferenceJoinFixture.Ast(database.Partition);
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits));
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var observed = new global::KeyLoad.Query.QueryExecution.ModelQueryExecutor(database.Database, engine)
            .Execute("root", ast, budget);
        await SqlEventReferenceJoinAssertions.PageAsync(observed, position);
        var observedBytes = budget.ReadBytes;
        await Assert.That(observedBytes).IsGreaterThan(1);
        var exact = Engine(database, database.Database.Limits with
        {
            MaxQueryReadBytes = observedBytes
        });
        await SqlEventReferenceJoinAssertions.PageAsync(exact.Execute("root",
            SqlEventReferenceJoinFixture.Request(database.Partition)), position);
        await SqlEventReferenceJoinAssertions.PageAsync(exact.ExecuteAst("root", ast), position);
        var oneLess = Engine(database, database.Database.Limits with
        {
            MaxQueryReadBytes = observedBytes - 1
        });
        await SqlEventReferenceJoinAssertions.RejectAsync(database, () => oneLess.Execute("root",
            SqlEventReferenceJoinFixture.Request(database.Partition)), ErrorCode.BudgetExceeded);
        await SqlEventReferenceJoinAssertions.RejectAsync(database,
            () => oneLess.ExecuteAst("root", ast), ErrorCode.BudgetExceeded);
    }

}
