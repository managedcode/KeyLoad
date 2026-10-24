using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class ProjectRowAuthorityCold
{
    internal static async Task RunAsync(TestDatabase db, CommandRequest command,
        CommitReceipt receipt, byte[] outcome, CancellationToken token)
    {
        var identity = db.Store.Identity;
        var cut = db.Store.Position;
        var image = CanonicalStoreImage.Bytes(db.Store);
        db.Store.Dispose();
        Exception? primary = null;
        var failures = new List<Exception>();
        try
        {
            using var cold = new ZoneTreeStore(new(db.Directory), db.StorageExecution,
                UnitExecutionOptions.PointCacheExecution(), timeProvider: db.Database.EvaluationClock);
            try
            {
                var create = TestDatabaseEngineFactory.Capture(UnitExecutionOptions.DatabaseLimits(),
                    UnitExecutionOptions.RuntimeClaimsExecution(), null, db.Database.EvaluationClock);
                var database = create(cold);
                var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
                await Assert.That(cold.Identity.NodeId).IsEqualTo(identity.NodeId);
                await Assert.That(cold.Identity.Incarnation).IsEqualTo(identity.Incarnation);
                await Assert.That(cold.Identity.ReadGeneration).IsGreaterThanOrEqualTo(identity.ReadGeneration);
                await Assert.That(cold.Position).IsEqualTo(cut);
                await Assert.That(CanonicalStoreImage.Bytes(cold).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
                await ProjectRowAuthorityAssertions.ReadAsync(db, engine, cold, true, token);
                await ProjectRowAuthorityContinuation.ReplayAsync(database, cold, command, receipt, outcome, token);
                await ProjectRowAuthorityAssertions.ReadAsync(db, engine, cold, true, token);
            }
            catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
            {
                primary = error;
                throw;
            }
            catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
            {
                primary = error;
                throw;
            }
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
        {
            Retain(primary, error, failures);
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
        {
            Retain(primary, error, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static void Retain(Exception? primary, Exception terminal, List<Exception> failures)
    {
        if (primary is not null && !ReferenceEquals(primary, terminal))
        {
            failures.Add(primary);
        }
        failures.Add(terminal);
    }
}
