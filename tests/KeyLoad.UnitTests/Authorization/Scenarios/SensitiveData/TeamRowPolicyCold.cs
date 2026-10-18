using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class TeamRowPolicyCold
{
    internal static async Task RunAsync(TestDatabase db, CommandRequest original, byte[] originalOutcome,
        CommandRequest current, CommitReceipt receipt, byte[] currentOutcome, CancellationToken token)
    {
        var identity = db.Store.Identity;
        var cut = db.Store.Position;
        var image = TeamRowPolicyAssertions.Image(db.Store);
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
                    UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), null, db.Database.EvaluationClock);
                var database = create(cold);
                var queries = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
                await Assert.That(cold.Identity.NodeId).IsEqualTo(identity.NodeId);
                await Assert.That(cold.Identity.Incarnation).IsEqualTo(identity.Incarnation);
                await Assert.That(cold.Identity.ReadGeneration).IsGreaterThanOrEqualTo(identity.ReadGeneration);
                await TeamRowPolicyAssertions.UnchangedAsync(cold, image, cut);
                await TeamRowPolicyAssertions.VisibleAsync(db, database, queries, cold,
                    TeamRowPolicyFlow.HealthyRevision, TeamRowPolicyFlow.HealthyJson, token);
                await TeamRowPolicyAssertions.OldRefusedAsync(database, cold, original, originalOutcome, ErrorCode.PermissionDenied, token);
                await TeamRowPolicyFlow.ReplayAsync(database, cold, current, receipt, currentOutcome, token);
                await TeamGraphVisibilityAssertions.ReadAsync(db, database, cold, db.Partition, true, token);
                await TeamRowPolicyAssertions.VisibleAsync(db, database, queries, cold,
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
