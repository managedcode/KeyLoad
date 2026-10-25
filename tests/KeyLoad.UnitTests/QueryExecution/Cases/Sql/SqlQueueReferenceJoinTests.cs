namespace KeyLoad.UnitTests.QueryExecution;

internal sealed class SqlQueueReferenceJoinTests
{
    [Test]
    public async Task QueueEntityReferenceJoinRefusesThenRepairsPersistedPolicyAndReplaysLiteralRowsAcrossTwoColdCuts()
    {
        var failures = new List<Exception>();
        try
        {
            using var database = new TestDatabase();
            try
            {
                var seed = SqlQueueReferenceJoinFixture.Seed(database);
                await SqlQueueReferenceJoinOperation.VerifyAsync(database);
                await SqlQueueReferenceJoinOperation.RefusalsAsync(database);
                await SqlQueueReferenceJoinPolicy.RunAsync(database);
                await SqlQueueReferenceJoinCold.RunAsync(database, seed.Command, seed.Receipt);
            }
            catch (Exception failure) when (global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (!global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
        }
        catch (Exception failure) when (global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
