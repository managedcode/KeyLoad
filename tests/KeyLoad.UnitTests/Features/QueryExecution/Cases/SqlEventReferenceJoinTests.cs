using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlEventReferenceJoinTests
{
    [Test]
    public async Task AcKl095EventEntityReferenceJoinRefusesThenRepairsPersistedPolicyAndReplaysLiteralRowsAcrossTwoColdCuts()
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
                await SqlEventReferenceJoinBudget.RunAsync(database);
                var repair = await SqlEventReferenceJoinPolicy.RunAsync(database);
                await SqlEventReferenceJoinCold.RunAsync(database, seed.Command, seed.Receipt, repair.Command, repair.Receipt);
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            { failures.Add(error); }
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
