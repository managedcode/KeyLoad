using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

internal static class ReplicaMaterializerLifecycleObservations
{
    private const string CanonicalDirectory = "canonical";
    private const string ReplicaDirectory = "replica";

    internal static async Task AssertReopenedAsync(ReplicaMaterializerLifecycleStores storeOwners,
        ReplicaConfiguration configuration, string directory, Action<List<Exception>> closeOwners, long cut)
    {
        var identity = storeOwners.Canonical.Identity;
        var failures = new List<Exception>();
        closeOwners(failures);
        ReplicaMaterializerLifecycleErrors.Throw(failures);
        using var reopenedCanonical = new ZoneTreeStore(new(Path.Combine(directory, CanonicalDirectory))
        {
            Incarnation = configuration.Incarnation
        }, RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        using var reopenedReplica = new ZoneTreeStore(new(Path.Combine(directory, ReplicaDirectory))
        {
            Incarnation = configuration.Incarnation
        }, RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        using var reopenedLog = new DurableReplicaLog(reopenedReplica, RecoveryExecutionOptions.Configuration(configuration));
        var database = new DatabaseEngine(reopenedCanonical, new AuthorizationPolicy(), RecoveryExecutionOptions.DatabaseLimits(), RecoveryExecutionOptions.DueWork(), RecoveryExecutionOptions.EventSource(), RecoveryExecutionOptions.Messaging(), RecoveryExecutionOptions.GraphExecution(), RecoveryExecutionOptions.ChangeFeedExecution(), RecoveryExecutionOptions.BlobExecution(), RecoveryExecutionOptions.NativeClaimsExecution(), RecoveryExecutionOptions.TimeSeriesExecution(), RecoveryExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance);
        await Assert.That(database.LastApplied).IsEqualTo(cut);
        await Assert.That(reopenedLog.State.CommittedIndex).IsEqualTo(cut);
        await Assert.That(reopenedLog.State.LastIndex).IsEqualTo(cut);
        await Assert.That(reopenedCanonical.Identity.NodeId).IsEqualTo(identity.NodeId);
        await Assert.That(reopenedCanonical.Identity.Incarnation).IsEqualTo(identity.Incarnation);
        await Assert.That(reopenedCanonical.Identity.ReadGeneration).IsEqualTo(identity.ReadGeneration);
    }

    internal static void JoinSynchronously(Func<Task> dispose, List<Exception> failures)
    {
        try
        {
            dispose().GetAwaiter().GetResult();
        }
        catch (IOException error)
        {
            ReplicaMaterializerLifecycleErrors.Add(error, failures);
        }
        catch (InvalidOperationException error)
        {
            ReplicaMaterializerLifecycleErrors.Add(error, failures);
        }
        catch (OperationCanceledException error)
        {
            ReplicaMaterializerLifecycleErrors.Add(error, failures);
        }
        catch (TimeoutException error)
        {
            ReplicaMaterializerLifecycleErrors.Add(error, failures);
        }
        catch (KeyLoadException error)
        {
            ReplicaMaterializerLifecycleErrors.Add(error, failures);
        }
    }

    internal static void CloseStores(ReplicaMaterializerLifecycleStores storeOwners, List<Exception> failures)
    {
        try
        {
            storeOwners.Dispose();
        }
        catch (AggregateException error)
        {
            ReplicaMaterializerLifecycleErrors.Add(error, failures);
        }
        catch (IOException error)
        {
            ReplicaMaterializerLifecycleErrors.Add(error, failures);
        }
    }
}
