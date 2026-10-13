namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlNullOrderingFlow
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var database = new TestDatabase();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var seed = SqlNullOrderingFixture.Seed(database);
                await SqlNullOrderingFixture.PolicyAsync(database, SqlNullOrderingProtocol.InitialEpoch, true);
                await SqlNullOrderingPages.HealthyAsync(database.Database, database.Store, database.Partition, SqlNullOrderingProtocol.Reader);
                await SqlNullOrderingRefusals.InvalidAsync(database);
                await SqlNullOrderingFixture.PolicyAsync(database, SqlNullOrderingProtocol.DeniedEpoch, false);
                await SqlNullOrderingRefusals.DeniedAsync(database);
                await SqlNullOrderingFixture.PolicyAsync(database, SqlNullOrderingProtocol.RepairedEpoch, true);
                await SqlNullOrderingPages.HealthyAsync(database.Database, database.Store, database.Partition, SqlNullOrderingProtocol.Reader);
                await SqlNullOrderingPages.ReplayAsync(database.Database, seed.Command, seed.Receipt);
                await SqlNullOrderingCold.RunAsync(database, seed.Command, seed.Receipt);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
