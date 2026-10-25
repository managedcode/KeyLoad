using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class SqlEventReferenceJoinBudget
{
    internal static async Task RunAsync(TestDatabase database)
    {
        await BytesAsync(database);
        await SqlEventReferenceJoinOperation.VerifyAsync(database);
    }

    private static QueryEngine Engine(TestDatabase database, global::KeyLoad.Query.QueryExecutionOptions execution)
    {
        var owner = new DatabaseEngine(database.Store, database.Database.Authorization,
            UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(),
            UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(),
            UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.RuntimeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution());
        return new(owner, UnitExecutionOptions.QueryExecution(execution));
    }

    private static async Task BytesAsync(TestDatabase database)
    {
        var before = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        await ResultBytesAsync(database, position);
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
        var exact = Engine(database, new global::KeyLoad.Query.QueryExecutionOptions { MaximumResultBytes = observedBytes });
        await SqlEventReferenceJoinAssertions.PageAsync(exact.Execute("root", request), position);
        await SqlEventReferenceJoinAssertions.PageAsync(exact.ExecuteAst("root",
            SqlEventReferenceJoinFixture.Ast(database.Partition)), position);
        var oneLess = Engine(database, new global::KeyLoad.Query.QueryExecutionOptions { MaximumResultBytes = observedBytes - 1 });
        await SqlEventReferenceJoinAssertions.RejectAsync(database,
            () => oneLess.Execute("root", request), ErrorCode.BudgetExceeded);
        await SqlEventReferenceJoinAssertions.RejectAsync(database,
            () => oneLess.ExecuteAst("root", SqlEventReferenceJoinFixture.Ast(database.Partition)), ErrorCode.BudgetExceeded);
    }
}
