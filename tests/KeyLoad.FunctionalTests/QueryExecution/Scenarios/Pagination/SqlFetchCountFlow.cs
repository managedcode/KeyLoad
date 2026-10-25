namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class SqlFetchCountFlow
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var database = new TestDatabase();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var seed = SqlFetchCountFixture.Seed(database);
                await SqlFetchCountFixture.PolicyAsync(database, SqlFetchCountProtocol.InitialEpoch, true);
                await SqlFetchCountPages.HealthyAsync(database.Database, database.Store, database.Partition, SqlFetchCountProtocol.Reader);
                await SqlFetchCountRefusals.InvalidAsync(database);
                await SqlFetchCountFixture.PolicyAsync(database, SqlFetchCountProtocol.DeniedEpoch, false);
                await SqlFetchCountRefusals.DeniedAsync(database);
                await SqlFetchCountFixture.PolicyAsync(database, SqlFetchCountProtocol.RepairedEpoch, true);
                await SqlFetchCountPages.HealthyAsync(database.Database, database.Store, database.Partition, SqlFetchCountProtocol.Reader);
                await SqlFetchCountPages.ReplayAsync(database.Database, seed.Command, seed.Receipt);
                await SqlFetchCountCold.RunAsync(database, seed.Command, seed.Receipt);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
