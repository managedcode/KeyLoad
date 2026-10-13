namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlOffsetFlow
{
    internal static async Task RunAsync(TestDatabase database)
    {
        var (seed, receipt) = SqlOffsetFixture.Seed(database);
        await SqlOffsetFixture.PolicyAsync(database, SqlOffsetProtocol.GrantEpoch, allow: true);
        var before = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        await SqlOffsetPages.HealthyAsync(database.Database, database.Store, database.Partition, SqlOffsetProtocol.Reader);
        await SqlOffsetRefusals.InvalidAsync(database);
        await SqlOffsetPages.ReplayAsync(database.Database, seed, receipt);
        await SqlOffsetRefusals.UnchangedAsync(database.Store, before, position, identity);
        await SqlOffsetFixture.PolicyAsync(database, SqlOffsetProtocol.DenyEpoch, allow: false);
        await SqlOffsetRefusals.DeniedAsync(database);
        await SqlOffsetFixture.PolicyAsync(database, SqlOffsetProtocol.RepairEpoch, allow: true);
        await SqlOffsetPages.HealthyAsync(database.Database, database.Store, database.Partition, SqlOffsetProtocol.Reader);
        await SqlOffsetCold.RunAsync(database, seed, receipt);
    }
}
