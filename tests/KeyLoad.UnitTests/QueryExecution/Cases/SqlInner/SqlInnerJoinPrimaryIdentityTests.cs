namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlInnerJoinPrimaryIdentityTests
{
    [Test]
    public async Task RetainedJsonPrimaryKeysCannotContradictNativeIdentityAndRepairAcrossTwoDirtyAndHealthyColdOwners()
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var database = new TestDatabase();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var seed = SqlInnerJoinPrimaryIdentityFixture.Seed(database);
                await SqlInnerJoinPrimaryIdentityFlow.RunAsync(database, seed.Command, seed.Receipt);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
