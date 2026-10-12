namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlInnerJoinPrimaryIdentityFlow
{
    internal static async Task RunAsync(TestDatabase database, global::KeyLoad.CommandRequest command, global::KeyLoad.CommitReceipt receipt)
    {
        var faults = SqlInnerJoinPrimaryIdentityFixture.Capture(database);
        await SqlInnerJoinRetainedIdentityPages.HealthyAsync(database.Database, database.Store, database.Partition, command, receipt);
        database.Store.Dispose();
        foreach (var fault in faults)
        { await SqlInnerJoinRetainedIdentityFlow.FaultAsync(database, command, receipt, fault); }
    }
}
