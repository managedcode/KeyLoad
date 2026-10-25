namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class ScriptDomSelectFlow
{
    internal static async Task RunAsync(TestDatabase database)
    {
        var (seed, receipt) = SqlOffsetFixture.Seed(database);
        await SqlOffsetFixture.PolicyAsync(database, SqlOffsetProtocol.GrantEpoch, true);
        await ScriptDomSelectPages.HealthyAsync(database.Database, database.Store, database.Partition);
        await ScriptDomSelectRefusals.RunAsync(database);
        await SqlOffsetPages.ReplayAsync(database.Database, seed, receipt);
        await SqlOffsetFixture.PolicyAsync(database, SqlOffsetProtocol.DenyEpoch, false);
        await ScriptDomSelectRefusals.DeniedAsync(database);
        await SqlOffsetFixture.PolicyAsync(database, SqlOffsetProtocol.RepairEpoch, true);
        await ScriptDomSelectPages.HealthyAsync(database.Database, database.Store, database.Partition);
        await SqlOffsetPages.ReplayAsync(database.Database, seed, receipt);
        await ScriptDomSelectCold.RunAsync(database, seed, receipt);
    }
}
