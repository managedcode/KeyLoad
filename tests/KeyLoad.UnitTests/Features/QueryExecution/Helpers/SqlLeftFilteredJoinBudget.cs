using KeyLoad.Core;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlLeftFilteredJoinBudget
{
    private const int TwoLeftRowsOnly = 2;

    internal static async Task RunAsync(TestDatabase database)
    {
        var engine = Engine(database);
        var request = SqlLeftFilteredJoinFixture.Request(database.Partition);
        await SqlLeftFilteredJoinAssertions.RejectAsync(database,
            () => engine.Execute("root", request), ErrorCode.BudgetExceeded);
        var falseFilter = request with
        {
            Sql = SqlLeftFilteredJoinFixture.Prefix + "WHERE l.total > 100 " + SqlLeftFilteredJoinFixture.Order,
            Parameters = null
        };
        var page = engine.Execute("root", falseFilter);
        await Assert.That(page.Rows).IsEmpty();
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.CutPosition).IsEqualTo(database.Store.Position);
        database.Commit(new PutDocument(SqlLeftFilteredJoinFixture.Left, "c",
            "{\"order_id\":\"c\",\"customer_id\":\"c1\",\"total\":1}"));
        await SqlLeftFilteredJoinAssertions.RejectAsync(database,
            () => engine.Execute("root", falseFilter), ErrorCode.BudgetExceeded);
        await SqlLeftFilteredJoinOperation.VerifyAsync(database);
    }

    private static QueryEngine Engine(TestDatabase database)
    {
        var limits = database.Database.Limits with { MaxScanRecords = TwoLeftRowsOnly };
        var owner = new DatabaseEngine(database.Store, database.Database.Authorization,
            UnitExecutionOptions.DatabaseLimits(limits), UnitExecutionOptions.DueWork(),
            UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(),
            UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(),
            UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution(),
            UnitExecutionOptions.MovementCheckpoints(), UnavailablePartitionMovementCheckpointVerifier.Instance);
        return new(owner, UnitExecutionOptions.QueryExecution());
    }
}
