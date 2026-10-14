using KeyLoad.Core;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlQueueReferenceJoinBudget
{
    private const int TwoSourceMetadataRowsOnly = 2;

    internal static async Task RunAsync(TestDatabase database)
    {
        var engine = Engine(database);
        var request = SqlQueueReferenceJoinFixture.Request(database.Partition);
        await SqlQueueReferenceJoinAssertions.RejectAsync(database,
            () => engine.Execute("root", request), ErrorCode.BudgetExceeded);
        var falseFilter = request with
        {
            Sql = request.Sql.Replace("q.payload.score >= @minimum", "q.payload.score > 100", StringComparison.Ordinal),
            Parameters = null
        };
        await SqlQueueReferenceJoinAssertions.RejectAsync(database,
            () => engine.Execute("root", falseFilter), ErrorCode.BudgetExceeded);
        await SqlQueueReferenceJoinOperation.VerifyAsync(database);
    }

    private static QueryEngine Engine(TestDatabase database)
    {
        var limits = database.Database.Limits with
        {
            MaxScanRecords = TwoSourceMetadataRowsOnly
        };
        var owner = new DatabaseEngine(database.Store, database.Database.Authorization,
            UnitExecutionOptions.DatabaseLimits(limits), UnitExecutionOptions.DueWork(),
            UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(),
            UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(),
            UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution(),
            UnitExecutionOptions.MovementCheckpoints(), UnavailablePartitionMovementCheckpointVerifier.Instance);
        return new(owner, UnitExecutionOptions.QueryExecution());
    }
}
