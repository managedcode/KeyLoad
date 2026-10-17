namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlModelEventRecordIntegrityTests
{
    [Test]
    public async Task AcSqlView002InvalidRetainedEventMetadataCannotBeHiddenByWhereOrLimitAndRepairsAcrossTwoColdCuts()
    {
        var failures = new List<Exception>();
        try
        {
            using var database = new TestDatabase();
            try
            {
                await SqlModelEventRecordIntegrityFlow.RunAsync(database);
            }
            catch (Exception error) when (global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        }
        catch (Exception error) when (global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
