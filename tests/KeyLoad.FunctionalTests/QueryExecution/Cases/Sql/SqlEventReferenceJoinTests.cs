using KeyLoad.Clustering;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class SqlEventReferenceJoinTests
{
    [Test]
    public async Task EventEntityReferenceJoinRefusesThenRepairsPersistedPolicyAndReplaysLiteralRowsAcrossTwoColdCuts()
    {
        var failures = new List<Exception>();
        try
        {
            using var database = new TestDatabase();
            try
            {
                var seed = SqlEventReferenceJoinFixture.Seed(database);
                await SqlEventReferenceJoinOperation.VerifyAsync(database);
                await SqlEventReferenceJoinOperation.RefusalsAsync(database);
                var repair = await SqlEventReferenceJoinPolicy.RunAsync(database);
                await SqlEventReferenceJoinCold.RunAsync(database, seed.Command, seed.Receipt, repair.Command, repair.Receipt);
            }
            catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
