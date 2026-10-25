using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class SqlJoinAliasCollisionTests
{
    [Test]
    public async Task RightAliasCollisionRefusesWithoutEffectsThenLeftPredicateReplaysAcrossTwoColdCuts()
    {
        var failures = new List<Exception>();
        try
        {
            using var database = new TestDatabase();
            await ServerFailureObserver.ObserveAsync(() => RunAsync(database), failures);
        }
        catch (Exception error) when (global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunAsync(TestDatabase database)
    {
        var seed = SqlQueueReferenceJoinFixture.Seed(database);
        await SqlJoinAliasCollisionFlow.RunAsync(database.Database, database.Store, database.Partition, repaired: false);
        await SqlQueueReferenceJoinPolicy.RunAsync(database);
        await SqlJoinAliasCollisionFlow.RunAsync(database.Database, database.Store, database.Partition, repaired: true);
        await SqlJoinAliasCollisionCold.RunAsync(database, seed.Command, seed.Receipt);
    }
}
