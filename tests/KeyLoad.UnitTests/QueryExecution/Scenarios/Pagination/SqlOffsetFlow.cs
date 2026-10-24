namespace KeyLoad.UnitTests.QueryExecution;

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
        await SqlTableControls.RefusalsAsync(database, new global::KeyLoad.Query.QueryEngine(
            database.Database, UnitExecutionOptions.QueryExecution()));
        await SqlOrdinalControls.RefusalsAsync(database, new global::KeyLoad.Query.QueryEngine(
            database.Database, UnitExecutionOptions.QueryExecution()));
        await SqlOffsetPages.ReplayAsync(database.Database, seed, receipt);
        await SqlOffsetRefusals.UnchangedAsync(database.Store, before, position, identity);
        await SqlOffsetFixture.PolicyAsync(database, SqlOffsetProtocol.DenyEpoch, allow: false);
        await SqlOffsetRefusals.DeniedAsync(database);
        await SqlTableControls.DeniedAsync(database, new global::KeyLoad.Query.QueryEngine(
            database.Database, UnitExecutionOptions.QueryExecution()));
        await SqlOrdinalControls.DeniedAsync(database, new global::KeyLoad.Query.QueryEngine(
            database.Database, UnitExecutionOptions.QueryExecution()));
        await SqlOffsetFixture.PolicyAsync(database, SqlOffsetProtocol.RepairEpoch, allow: true);
        await SqlOffsetPages.HealthyAsync(database.Database, database.Store, database.Partition, SqlOffsetProtocol.Reader);
        await SqlOffsetCold.RunAsync(database, seed, receipt);
    }
}
