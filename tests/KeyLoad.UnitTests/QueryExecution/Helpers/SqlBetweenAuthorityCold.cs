using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Authorization;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlBetweenAuthorityCold
{
    internal static async Task RunAsync(TestDatabase db)
    {
        var identity = db.Store.Identity;
        var image = TeamRowPolicyAssertions.Image(db.Store);
        var cut = db.Store.Position;
        db.Store.Dispose();
        var failures = new List<Exception>();
        try
        {
            using var cold = new ZoneTreeStore(new(db.Directory), db.StorageExecution,
                UnitExecutionOptions.PointCacheExecution(), timeProvider: db.Database.EvaluationClock);
            try
            {
                var database = db.CreateConfiguredDatabase(cold);
                await Assert.That(cold.Identity.NodeId).IsEqualTo(identity.NodeId);
                await Assert.That(cold.Identity.Incarnation).IsEqualTo(identity.Incarnation);
                await Assert.That(cold.Identity.ReadGeneration).IsGreaterThanOrEqualTo(identity.ReadGeneration);
                await TeamRowPolicyAssertions.UnchangedAsync(cold, image, cut);
                await SqlBetweenAuthorityResults.PageAsync(db, database, cold, raw: true);
                await SqlBetweenAuthorityContinuation.ConfigureAsync(database, SqlBetweenAuthorityContinuation.ColdReducedEpoch, raw: false);
                await SqlBetweenAuthorityResults.PageAsync(db, database, cold, raw: false);
                await SqlBetweenAuthorityContinuation.ConfigureAsync(database, SqlBetweenAuthorityContinuation.ColdRepairEpoch, raw: true);
                await SqlBetweenAuthorityResults.PageAsync(db, database, cold, raw: true);
            }
            catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
            {
                failures.Add(error);
            }
            catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
            {
                failures.Add(error);
            }
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
        {
            failures.Add(error);
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
        {
            failures.Add(error);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
