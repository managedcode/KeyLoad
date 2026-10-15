using System.Runtime.ExceptionServices;
using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Security;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class HostReplicaStores : IDisposable
{
    private readonly ZoneTreeStore replica;
    private readonly PartitionHostRecoveryAttempt attempt;
    private bool closeAttempted;
    private ExceptionDispatchInfo? originalCloseFailure;

    internal HostReplicaStores(NodeOptions options, PartitionHostRecoveryAttempt attempt)
    {
        this.attempt = attempt;
        Configuration = options.CreateReplicaConfiguration(options.DataDirectory);
        try
        {
            Canonical = Open(options, PartitionHostRecoveryFixture.CanonicalDirectory, attempt.Ownership.Canonical);
            replica = Open(options, ReplicaProtocol.ReplicaDirectory, attempt.Ownership.Replica);
            attempt.Ownership.Host.BeginOpen();
            attempt.Ownership.Host.BeginNativeConstructor();
            Log = new(replica, UnitExecutionOptions.ReplicaConfiguration(Configuration));
            attempt.Ownership.Host.NativeConstructorReturned();
            Database = new(Canonical, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution(), UnitExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance);
            Snapshots = new(Canonical, Log, UnitExecutionOptions.ReplicaConfiguration(Configuration), UnitExecutionOptions.ReplicaExecution());
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            CloseKnown(failures);
            RecordAndThrow(failures);
            throw;
        }
    }

    internal ReplicaConfiguration Configuration
    {
        get;
    }
    internal ZoneTreeStore Canonical
    {
        get;
    }
    internal DurableReplicaLog Log
    {
        get;
    }
    internal DatabaseEngine Database
    {
        get;
    }
    internal ReplicaSnapshotStore Snapshots
    {
        get;
    }

    private static ZoneTreeStore Open(NodeOptions options, string suffix, NativeStorageOwnerSettlement settlement)
        => ZoneTreeStore.OpenOwned(new(Path.Combine(options.DataDirectory, suffix))
        {
            Incarnation = options.Incarnation,
            SigningKey = Convert.FromBase64String(options.SigningKey)
        }, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution(), settlement);

    public void Dispose()
    {
        if (closeAttempted)
        {
            originalCloseFailure?.Throw();
            attempt.RequireReleased();
            return;
        }
        closeAttempted = true;
        var failures = new List<Exception>();
        CloseKnown(failures);
        RecordAndThrow(failures);
        attempt.RequireReleased();
    }

    private void CloseKnown(List<Exception> failures)
    {
        var before = failures.Count;
        if (Log is not null)
        {
            ServerFailureObserver.Observe(Log.Dispose, failures);
        }
        try
        {
            replica?.Dispose();
        }
        catch (Exception failure) when (KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        if (Canonical is not null)
        {
            ServerFailureObserver.Observe(Canonical.Dispose, failures);
        }
        if (failures.Count == before)
        {
            attempt.Ownership.Host.ReleaseReturned();
        }
    }

    private void RecordAndThrow(List<Exception> failures)
    {
        try
        {
            ServerFailureObserver.ThrowIfAny(failures);
        }
        catch (Exception failure)
        {
            originalCloseFailure = ExceptionDispatchInfo.Capture(failure);
            attempt.RecordFailure(failure);
            throw;
        }
    }
}
