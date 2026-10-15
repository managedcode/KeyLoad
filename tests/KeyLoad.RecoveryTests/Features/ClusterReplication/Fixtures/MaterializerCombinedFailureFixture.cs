using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.Replication;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

internal sealed class MaterializerCombinedFailureFixture : IAsyncDisposable
{
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ZoneTreeStore replica;
    internal ReplicaConfiguration Configuration
    {
        get;
    }
    private readonly ZoneTreeStore canonical;
    private bool armed;
    internal long ExpectedRecoveredPosition
    {
        get; private set;
    }
    private bool originalJoined;
    private bool retainOnFailure;
    private FileNotFoundException? physicalFailure;
    private Task? terminal;
    internal Exception CancellationFailure { get; } = new InvalidOperationException(MaterializerCombinedFailureProtocol.CallbackDetail);
    internal ReplicaMaterializer Materializer
    {
        get;
    }
    internal DatabaseEngine Database
    {
        get;
    }
    internal DurableReplicaLog Log
    {
        get;
    }
    internal string Directory { get; } = ReplicaFixturePaths.NewDirectory(MaterializerCombinedFailureProtocol.DirectoryPrefix);
    internal string MissingFile => Path.Combine(Directory, MaterializerCombinedFailureProtocol.MissingFile);
    internal Task Entered => entered.Task;
    internal FileNotFoundException? PhysicalFailure => Volatile.Read(ref physicalFailure);

    internal MaterializerCombinedFailureFixture()
    {
        ZoneTreeStore? openedReplica = null;
        DurableReplicaLog? openedLog = null;
        ZoneTreeStore? openedCanonical = null;
        try
        {
            Configuration = new(ReplicaTermMetadataFixture.VoterA,
                [ReplicaTermMetadataFixture.VoterA, ReplicaTermMetadataFixture.VoterB, ReplicaTermMetadataFixture.VoterC],
                Path.Combine(Directory, MaterializerCombinedFailureProtocol.ReplicaDirectory), Guid.NewGuid());
            Configuration.Validate();
            canonical = openedCanonical = new(new(Path.Combine(Directory, MaterializerCombinedFailureProtocol.FaultCanonicalDirectory))
            {
                Incarnation = Configuration.Incarnation,
                FaultObserver = Observe
            }, RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
            Database = OpenDatabase(canonical);
            Database.Bootstrap(new(MaterializerCombinedFailureProtocol.Principal, MaterializerCombinedFailureProtocol.Tenant,
                [new(MaterializerCombinedFailureProtocol.Wildcard, MaterializerCombinedFailureProtocol.Wildcard, Capability.All)],
                [MaterializerCombinedFailureProtocol.Wildcard])
            {
                ClusterAdministrator = true
            },
                DatabaseEngine.Credential(MaterializerCombinedFailureProtocol.Principal,
                    MaterializerCombinedFailureProtocol.Principal, MaterializerCombinedFailureProtocol.Credential));
            RecoveryPhysicalShardBootstrap.Bootstrap(Database, MaterializerCombinedFailureProtocol.Principal, Configuration.VoterIds);
            replica = openedReplica = new(new(Configuration.Directory)
            {
                Incarnation = Configuration.Incarnation
            }, RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
            Log = openedLog = new(replica, RecoveryExecutionOptions.Configuration(Configuration), canonicalDatabase: Database);
            Materializer = new(Database, Log, new ReplicaSnapshotStore(canonical, Log,
                RecoveryExecutionOptions.Configuration(Configuration), RecoveryExecutionOptions.Replica()), RecoveryExecutionOptions.Replica());
        }
        catch (Exception primary)
        {
            List<Exception> failures = [primary];
            ReplicaMaterializerLifecycleErrors.Attempt(() => openedCanonical?.Dispose(), failures);
            ReplicaMaterializerLifecycleErrors.Attempt(() => openedLog?.Dispose(), failures);
            ReplicaMaterializerLifecycleErrors.Attempt(() => openedReplica?.Dispose(), failures);
            ReplicaMaterializerLifecycleErrors.Attempt(() => System.IO.Directory.Delete(Directory, true), failures);
            ReplicaMaterializerLifecycleErrors.Throw(failures);
            throw;
        }
    }

    internal void ArmCommittedApply()
    {
        Log.SaveTermAndVote(MaterializerCombinedFailureProtocol.Term, Configuration.LocalId);
        Log.Append([new(MaterializerCombinedFailureProtocol.FaultIndex, MaterializerCombinedFailureProtocol.Term, null)]);
        ExpectedRecoveredPosition = checked(canonical.Position + MaterializerCombinedFailureProtocol.NextPosition);
        armed = true;
        Materializer.Commit(MaterializerCombinedFailureProtocol.FaultIndex);
    }

    private void Observe(CommitStage stage, long position, int mutation)
    {
        if (!armed || stage != CommitStage.JournalFlushed)
        {
            return;
        }
        armed = false;
        entered.TrySetResult();
        released.Task.GetAwaiter().GetResult();
        try
        {
            using var original = File.OpenRead(MissingFile);
            throw new InvalidOperationException(MaterializerCombinedFailureProtocol.MissingFileUnexpected);
        }
        catch (FileNotFoundException original)
        {
            Volatile.Write(ref physicalFailure, original);
            throw;
        }
    }

    internal void RetainOnFailure() => retainOnFailure = true;

    internal Task BeginShutdown() => terminal ??= Materializer.DisposeAsync().AsTask();
    internal void Release() => released.TrySetResult();
    internal void MarkOriginalJoined(Task actual)
    {
        if (!ReferenceEquals(actual, terminal) || !actual.IsFaulted || !actual.IsCompleted)
        {
            throw new InvalidOperationException(MaterializerCombinedFailureProtocol.JoinNotProven);
        }
        originalJoined = true;
    }

    internal void CloseStores()
    {
        if (!originalJoined)
        {
            throw new InvalidOperationException(MaterializerCombinedFailureProtocol.JoinNotProven);
        }
        List<Exception> failures = [];
        ReplicaMaterializerLifecycleErrors.Attempt(canonical.Dispose, failures);
        ReplicaMaterializerLifecycleErrors.Attempt(Log.Dispose, failures);
        ReplicaMaterializerLifecycleErrors.Attempt(replica.Dispose, failures);
        ReplicaMaterializerLifecycleErrors.Throw(failures);
    }

    internal static DatabaseEngine OpenDatabase(ZoneTreeStore store)
        => new(store, new AuthorizationPolicy(), RecoveryExecutionOptions.DatabaseLimits(), RecoveryExecutionOptions.DueWork(),
            RecoveryExecutionOptions.EventSource(), RecoveryExecutionOptions.Messaging(), RecoveryExecutionOptions.GraphExecution(),
            RecoveryExecutionOptions.ChangeFeedExecution(), RecoveryExecutionOptions.BlobExecution(), RecoveryExecutionOptions.NativeClaimsExecution(),
            RecoveryExecutionOptions.TimeSeriesExecution(), RecoveryExecutionOptions.MovementCheckpoints(),
            KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance);

    public async ValueTask DisposeAsync()
    {
        Release();
        List<Exception> failures = [];
        if (!originalJoined)
        {
            await ReplicaMaterializerLifecycleErrors.AttemptAsync(BeginShutdown, failures);
        }
        ReplicaMaterializerLifecycleErrors.Attempt(canonical.Dispose, failures);
        ReplicaMaterializerLifecycleErrors.Attempt(Log.Dispose, failures);
        ReplicaMaterializerLifecycleErrors.Attempt(replica.Dispose, failures);
        if (!retainOnFailure && failures.Count == MaterializerCombinedFailureProtocol.NoFailures)
        {
            ReplicaMaterializerLifecycleErrors.Attempt(() => System.IO.Directory.Delete(Directory, true), failures);
        }
        ReplicaMaterializerLifecycleErrors.Throw(failures);
    }
}
