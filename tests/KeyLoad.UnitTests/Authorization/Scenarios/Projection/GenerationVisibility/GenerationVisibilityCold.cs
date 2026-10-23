using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class GenerationVisibilityCold
{
    internal static async Task RunAsync(TestDatabase db, CacheMemoryBudget memory, CommandRequest original,
        byte[] originalOutcome, CommandRequest current, CommitReceipt receipt, byte[] currentOutcome, Func<DatabaseEngine, ZoneTreeStore, CancellationToken, Task> verifyWrites, CancellationToken token)
    {
        var identity = db.Store.Identity;
        var image = TeamRowPolicyAssertions.Image(db.Store);
        var cut = db.Store.Position;
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
                using var cache = database.ConfigureRowVisibility(memory);
                await Assert.That(memory.GetSnapshot().RetainedEntries).IsEqualTo(0);
                await Assert.That(cold.Identity.NodeId).IsEqualTo(identity.NodeId);
                await Assert.That(cold.Identity.Incarnation).IsEqualTo(identity.Incarnation);
                await Assert.That(cold.Identity.ReadGeneration).IsGreaterThanOrEqualTo(identity.ReadGeneration);
                await TeamRowPolicyAssertions.UnchangedAsync(cold, image, cut);
                var queries = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
                await GenerationVisibilityAssertions.HealthyAsync(db, database, queries, cold,
                    TeamRowPolicyFlow.HealthyRevision, TeamRowPolicyFlow.HealthyJson, token);
                await verifyWrites(database, cold, token);
                await TeamRowPolicyAssertions.OldRefusedAsync(database, cold, original, originalOutcome, ErrorCode.PermissionDenied, token);
                await TeamRowPolicyFlow.ReplayAsync(database, cold, current, receipt, currentOutcome, token);
                await GenerationVisibilityAssertions.HealthyAsync(db, database, queries, cold,
                    TeamRowPolicyFlow.HealthyRevision, TeamRowPolicyFlow.HealthyJson, token);
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
