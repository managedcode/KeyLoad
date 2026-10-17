using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class NestedSensitiveCurrentPolicyCold
{
    internal static async Task RunAsync(TestDatabase db, CommandRequest command, CommitReceipt original,
        CancellationToken token)
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
                var database = db.CreateConfiguredDatabase(cold);
                await Assert.That(cold.Identity.NodeId).IsEqualTo(identity.NodeId);
                await Assert.That(cold.Identity.Incarnation).IsEqualTo(identity.Incarnation);
                await Assert.That(cold.Identity.ReadGeneration).IsGreaterThanOrEqualTo(identity.ReadGeneration);
                await TeamRowPolicyAssertions.UnchangedAsync(cold, image, cut);
                await NestedSensitiveCurrentPolicyContinuation.ReplayAsync(database, cold, command, original, token);
                await NestedSensitiveCurrentPolicyAssertions.CurrentAsync(db, database, cold, token);
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
