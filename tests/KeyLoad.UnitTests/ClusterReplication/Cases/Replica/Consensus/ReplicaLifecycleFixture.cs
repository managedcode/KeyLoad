using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Replication;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests;

internal sealed class ReplicaLifecycleFixture : IAsyncDisposable
{
    private const string Prefix = "keyload-replica-lifecycle-";
    private const int NoFailures = 0;
    private const string GuidFormat = "N";
    private const string CanonicalDirectory = "canonical";
    private const string ReplicaDirectory = "replica";
    private const string VoterA = "a";
    private const string VoterB = "b";
    private const string VoterC = "c";
    private readonly string directory;
    private readonly bool ownsDirectory;
    private readonly ZoneTreeStore canonical;

    internal ReplicaLifecycleFixture(ReplicaExecutionOptions? executionSettings = null, int? maximumAppendEntries = null,
        Action<CommitStage, long, int>? canonicalObserver = null, ReplicaConfiguration? configuration = null,
        ReadOnlyMemory<byte>? canonicalSigningKey = null,
        Func<ZoneTreeStore, DatabaseEngine>? canonicalDatabaseFactory = null, TimeProvider? evaluationClock = null,
        string? borrowedDirectory = null)
    {
        ownsDirectory = borrowedDirectory is null;
        directory = borrowedDirectory is null
            ? Path.Combine(Resolve(new(Path.GetTempPath())), Prefix + Guid.NewGuid().ToString(GuidFormat))
            : Resolve(new(borrowedDirectory));
        Configuration = Configure(configuration, directory, maximumAppendEntries);
        ZoneTreeStore? acquiredCanonical = null;
        ZoneTreeStore? acquiredReplica = null;
        DurableReplicaLog? acquiredLog = null;
        ReplicaMaterializer? acquiredMaterializer = null;
        ReplicaConsensus? acquiredConsensus = null;
        try
        {
            canonical = acquiredCanonical = new(new(Path.Combine(directory, CanonicalDirectory))
            {
                Incarnation = Configuration.Incarnation,
                SigningKey = canonicalSigningKey,
                FaultObserver = canonicalObserver
            }, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
            ReplicaStore = acquiredReplica = new(new(Path.Combine(directory, ReplicaDirectory))
            {
                Incarnation = Configuration.Incarnation
            }, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
            Database = canonicalDatabaseFactory?.Invoke(canonical) ?? new(canonical, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(),
                UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution(), UnitExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance);
            Log = acquiredLog = new(ReplicaStore, ReplicaExecutionTestOptions.Configuration(Configuration), canonicalDatabase: Database);
            ExecutionOptions = ReplicaExecutionTestOptions.Execution(executionSettings);
            Materializer = acquiredMaterializer = new(Database, Log, new ReplicaSnapshotStore(canonical, Log,
                ReplicaExecutionTestOptions.Configuration(Configuration), ExecutionOptions, replacementToken => Database.RequireNoRetirementForSnapshotReplacement(
                    new(Database.OperationLimitsOptions, Database.EvaluationClock, replacementToken))), ExecutionOptions);
            Consensus = acquiredConsensus = new(Materializer, ReplicaExecutionTestOptions.Configuration(Configuration), ExecutionOptions, evaluationClock ?? TimeProvider.System);
        }
        catch (Exception primary)
        {
            Rollback(primary, acquiredConsensus, acquiredMaterializer, acquiredLog, acquiredReplica, acquiredCanonical);
            throw;
        }
    }

    internal string CanonicalDirectoryPath => Path.Combine(directory, CanonicalDirectory);

    internal Microsoft.Extensions.Options.IOptions<ReplicaExecutionOptions> ExecutionOptions
    {
        get;
    }

    internal ReplicaConfiguration Configuration
    {
        get;
    }
    internal ZoneTreeStore ReplicaStore
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
    internal ReplicaMaterializer Materializer
    {
        get;
    }
    internal ReplicaConsensus Consensus
    {
        get;
    }

    private static ReplicaConfiguration Configure(ReplicaConfiguration? configured, string directory, int? maximumAppendEntries)
    {
        var configuration = (configured ?? new(VoterA, [VoterA, VoterB, VoterC], directory, Guid.NewGuid()))
            with
        {
            Directory = directory
        };
        return maximumAppendEntries is { } maximum
            ? configuration with
            {
                MaxAppendEntries = maximum
            } : configuration;
    }

    private static void Rollback(Exception primary, ReplicaConsensus? consensus, ReplicaMaterializer? materializer,
        DurableReplicaLog? log, ZoneTreeStore? replica, ZoneTreeStore? canonical)
    {
        var failures = new List<Exception> { primary };
        if (consensus is not null)
        {
            KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => consensus.DisposeAsync().AsTask(), failures).GetAwaiter().GetResult();
        }
        if (materializer is not null)
        {
            KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => materializer.DisposeAsync().AsTask(), failures).GetAwaiter().GetResult();
        }
        if (log is not null)
        {
            KeyLoad.Server.ServerFailureObserver.Observe(log.Dispose, failures);
        }
        if (replica is not null)
        {
            KeyLoad.Server.ServerFailureObserver.Observe(replica.Dispose, failures);
        }
        if (canonical is not null)
        {
            KeyLoad.Server.ServerFailureObserver.Observe(canonical.Dispose, failures);
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static string Resolve(DirectoryInfo directory)
    {
        if (directory.Parent is null)
        {
            return directory.FullName;
        }
        var entry = new DirectoryInfo(Path.Combine(Resolve(directory.Parent), directory.Name));
        return entry.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? entry.FullName;
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => Consensus.DisposeAsync().AsTask(), failures);
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => Materializer.DisposeAsync().AsTask(), failures);
        KeyLoad.Server.ServerFailureObserver.Observe(Log.Dispose, failures);
        KeyLoad.Server.ServerFailureObserver.Observe(ReplicaStore.Dispose, failures);
        try
        {
            canonical.Dispose();
        }
        catch (Exception failure) when (global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        if (ownsDirectory && failures.Count == NoFailures)
        {
            KeyLoad.Server.ServerFailureObserver.Observe(() => Directory.Delete(directory, true), failures);
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
