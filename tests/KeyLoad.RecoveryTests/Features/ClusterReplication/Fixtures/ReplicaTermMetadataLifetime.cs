using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

// Owns the independent physical replica and canonical handles borrowed by the term fixture.
internal sealed class ReplicaTermMetadataLifetime : IDisposable
{
    private const string ReplicaDirectory = "replica";
    private const string CanonicalDirectory = "canonical";
    private ZoneTreeStore ReplicaStore { get; }
    private readonly ZoneTreeStore canonicalStore;
    private DurableReplicaLog ReplicaLog { get; }
    private bool disposed;
    private bool replicaClosed;
    private bool logClosed;

    private ReplicaTermMetadataLifetime(ZoneTreeStore replica, ZoneTreeStore canonical, DatabaseEngine database,
        DurableReplicaLog log, ReplicaConfiguration configuration)
    {
        ReplicaStore = replica;
        canonicalStore = canonical;
        ReplicaLog = log;
        Database = database;
        Configuration = configuration;
    }

    // Borrowers may observe or explicitly close these handles; this lifetime retains cleanup ownership.
    internal ZoneTreeStore BorrowReplica() => ReplicaStore;
    internal DurableReplicaLog BorrowLog() => ReplicaLog;
    internal DatabaseEngine Database { get; }
    internal ReplicaConfiguration Configuration { get; }

    internal static ReplicaTermMetadataLifetime Open(string directory, Action<CommitStage, long, int>? observer)
    {
        ZoneTreeStore? replica = null;
        ZoneTreeStore? canonical = null;
        DurableReplicaLog? log = null;
        try
        {
            var configuration = new ReplicaConfiguration(ReplicaTermMetadataFixture.VoterA,
                [ReplicaTermMetadataFixture.VoterA, ReplicaTermMetadataFixture.VoterB, ReplicaTermMetadataFixture.VoterC],
                Path.Combine(directory, ReplicaDirectory), Guid.NewGuid());
            replica = new(new(configuration.Directory) { Incarnation = configuration.Incarnation, FaultObserver = observer }, RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
            canonical = new(new(Path.Combine(directory, CanonicalDirectory)) { Incarnation = configuration.Incarnation }, RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
            var database = new DatabaseEngine(canonical, new AuthorizationPolicy(), RecoveryExecutionOptions.DatabaseLimits(), RecoveryExecutionOptions.DueWork(), RecoveryExecutionOptions.EventSource(), RecoveryExecutionOptions.Messaging(), RecoveryExecutionOptions.GraphExecution(), RecoveryExecutionOptions.ChangeFeedExecution(), RecoveryExecutionOptions.BlobExecution(), RecoveryExecutionOptions.NativeClaimsExecution(), RecoveryExecutionOptions.TimeSeriesExecution(), RecoveryExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance);
            log = new(replica, RecoveryExecutionOptions.Configuration(configuration), canonicalDatabase: database);
            return new(replica, canonical, database, log, configuration);
        }
        catch (Exception original)
        {
            List<Exception> failures = [original];
            ReplicaMaterializerLifecycleErrors.Attempt(() => log?.Dispose(), failures);
            ReplicaMaterializerLifecycleErrors.Attempt(() => canonical?.Dispose(), failures);
            ReplicaMaterializerLifecycleErrors.Attempt(() => replica?.Dispose(), failures);
            ReplicaMaterializerLifecycleErrors.Attempt(() => Directory.Delete(directory, true), failures);
            ReplicaMaterializerLifecycleErrors.Throw(failures);
            throw;
        }
    }

    internal void CloseReplica()
    {
        ReplicaStore.Dispose();
        replicaClosed = true;
    }

    internal void CloseLog()
    {
        ReplicaLog.Dispose();
        logClosed = true;
    }

    public void Dispose()
    {
        if (disposed)
        { return; }
        disposed = true;
        List<Exception> failures = [];
        if (!logClosed)
        {
            try
            {
                try
                { ReplicaLog.Dispose(); }
                catch (Exception original)
                { throw new AggregateException(original); }
            }
            catch (AggregateException wrapper)
            { ReplicaMaterializerLifecycleErrors.AddWrapped(wrapper, failures); }
        }
        try
        {
            try
            { canonicalStore.Dispose(); }
            catch (Exception original)
            { throw new AggregateException(original); }
        }
        catch (AggregateException wrapper)
        { ReplicaMaterializerLifecycleErrors.AddWrapped(wrapper, failures); }
        if (!replicaClosed)
        {
            try
            {
                try
                { ReplicaStore.Dispose(); }
                catch (Exception original)
                { throw new AggregateException(original); }
            }
            catch (AggregateException wrapper)
            { ReplicaMaterializerLifecycleErrors.AddWrapped(wrapper, failures); }
        }
        // The fixture unwraps these original faults and still attempts directory cleanup.
        if (failures.Count > 0)
        { throw new AggregateException(failures); }
    }
}
