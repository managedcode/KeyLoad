using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Security;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;

namespace KeyLoad.CrashHost;

/// <summary>Owns real canonical and replica stores for a physical crash-test node.</summary>
internal sealed class ReplicaCrashNode : IDisposable
{
    private const string CanonicalDirectory = "database";
    private const string ReplicaDirectory = "replica";
    private const string TargetDirectory = "target";
    private const string SourceDirectory = "source";
    private const string VoterA = "a";
    private const string VoterB = "b";
    private const string VoterC = "c";
    private const string System = "system";
    private const string Wildcard = "*";
    private const string Obsolete = "replica-crash-obsolete";
    private const int CredentialBytes = 32;
    private readonly IOptions<ReplicaConfiguration> configurationOptions;

    private ReplicaCrashNode(string directory, string voter, Guid incarnation, Action<ReplicaCrashBoundary>? observer,
        Action<CommitStage, long, int>? replicaObserver = null, ReadOnlyMemory<byte>? canonicalSigningKey = null)
    {
        configurationOptions = CrashExecutionOptions.Configuration(voter, [VoterA, VoterB, VoterC], directory, incarnation);
        Canonical = new(new(Path.Combine(directory, CanonicalDirectory)) { Incarnation = incarnation, SigningKey = canonicalSigningKey }, CrashExecutionOptions.StorageExecution(), CrashExecutionOptions.PointCacheExecution());
        try
        {
            ReplicaStore = new(new(Path.Combine(directory, ReplicaDirectory)) { Incarnation = incarnation, FaultObserver = replicaObserver }, CrashExecutionOptions.StorageExecution(), CrashExecutionOptions.PointCacheExecution());
            try
            {
                Database = new(Canonical, new AuthorizationPolicy(), CrashExecutionOptions.DatabaseLimits(), CrashExecutionOptions.DueWork(), CrashExecutionOptions.EventSource(), CrashExecutionOptions.Messaging(), CrashExecutionOptions.GraphExecution(), CrashExecutionOptions.ChangeFeedExecution(), CrashExecutionOptions.BlobExecution(), CrashExecutionOptions.NativeClaimsExecution(), CrashExecutionOptions.TimeSeriesExecution());
                Log = new(ReplicaStore, configurationOptions, observer, canonicalDatabase: Database);
                Bootstrap();
                Snapshots = new(Canonical, Log, configurationOptions, CrashExecutionOptions.Replica(), observer);
            }
            catch (Exception primary)
            {
                var failures = new List<Exception> { primary };
                if (Log is { } log)
                { ServerFailureObserver.Observe(log.Dispose, failures); }
                try
                { ReplicaStore.Dispose(); }
                catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null) { failures.Add(error); }
                catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null) { failures.Add(error); }
                ServerFailureObserver.ThrowIfAny(failures);
                throw;
            }
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(Canonical.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    /// <summary>The configured fixed RF3 voter set and stable cluster incarnation.</summary>
    public ReplicaConfiguration Configuration => configurationOptions.Value;
    /// <summary>The node-owned canonical storage engine.</summary>
    public ZoneTreeStore Canonical { get; }
    /// <summary>The independently owned native replica store, borrowed for GC operation assertions.</summary>
    public ZoneTreeStore ReplicaStore { get; }
    /// <summary>The real caller-visible database engine.</summary>
    public DatabaseEngine Database { get; }
    /// <summary>The independently persisted durable replica log.</summary>
    public DurableReplicaLog Log { get; }
    /// <summary>The real verified snapshot transfer implementation.</summary>
    public ReplicaSnapshotStore Snapshots { get; }
    /// <summary>The voter used for the durable vote scenario.</summary>
    public static string CandidateId => VoterB;

    /// <summary>Opens the exact physical target directory, without running recovery or materialization.</summary>
    /// <param name="root">The owning trial's physical root.</param>
    /// <param name="incarnation">The persisted cluster incarnation.</param>
    /// <param name="observer">The synchronous observer that pauses only at the armed durable boundary.</param>
    /// <param name="replicaObserver">Existing native replica-store publication observer for real process cuts.</param>
    /// <param name="canonicalSigningKey">Optional actual cluster key configured for a fresh receiving member and its reopen.</param>
    public static ReplicaCrashNode OpenTarget(string root, Guid incarnation, Action<ReplicaCrashBoundary>? observer = null,
        Action<CommitStage, long, int>? replicaObserver = null, ReadOnlyMemory<byte>? canonicalSigningKey = null)
        => new(Path.Combine(root, TargetDirectory), VoterA, incarnation, observer, replicaObserver, canonicalSigningKey);

    /// <summary>Opens a distinct real source node for verified snapshot transfer.</summary>
    /// <param name="root">The owning trial's physical root.</param>
    /// <param name="incarnation">The incarnation shared with the receiving node.</param>
    public static ReplicaCrashNode OpenSource(string root, Guid incarnation)
        => new(Path.Combine(root, SourceDirectory), VoterB, incarnation, null);

    /// <summary>The target directories whose exclusive ownership must be released after killing the child.</summary>
    /// <param name="root">The owning trial's physical root.</param>
    public static string[] TargetStoreDirectories(string root)
        => [Path.Combine(root, TargetDirectory, CanonicalDirectory), Path.Combine(root, TargetDirectory, ReplicaDirectory)];

    /// <summary>The source directories owned by snapshot-transfer crash scenarios.</summary>
    /// <param name="root">The owning trial's physical root.</param>
    public static string[] SourceStoreDirectories(string root)
        => [Path.Combine(root, SourceDirectory, CanonicalDirectory), Path.Combine(root, SourceDirectory, ReplicaDirectory)];

    /// <summary>Whether the exact crash scenario opens its independent source node.</summary>
    /// <param name="boundary">The already validated physical crash boundary.</param>
    public static bool RequiresSourceStores(ReplicaCrashBoundary boundary)
        => boundary is ReplicaCrashBoundary.SnapshotVerified or ReplicaCrashBoundary.SnapshotInstalled
            or ReplicaCrashBoundary.SnapshotTransferBegun or ReplicaCrashBoundary.SnapshotChunkAcknowledged
            or ReplicaCrashBoundary.SnapshotRejected or ReplicaCrashBoundary.SnapshotPublished;

    /// <summary>Builds a genuine committed canonical prefix from database operations.</summary>
    /// <param name="cut">The final deterministic operation to append, commit and apply.</param>
    public void Populate(int cut)
    {
        const int SeedTerm = 1;
        const int FirstSeedOperation = 1;
        const int ConfigureResourceOperation = 1;

        Log.SaveTermAndVote(SeedTerm, null);
        for (var index = FirstSeedOperation; index <= cut; index++)
        {
            var operation = Database.NormalizeOperation(ReplicaCrashModel.Operation(index));
            Log.Append([new(index, SeedTerm, operation)]);
            Log.Commit(index);
            var result = Database.Apply(operation, index);
            if (index == ConfigureResourceOperation)
            {
                result.Get<ResourceDefinition>();
            }
            else
            {
                result.Get<CommitReceipt>();
            }
        }
    }

    /// <summary>Adds target-only state that a canonical replacement must remove.</summary>
    public void AddObsoleteRecord()
        => Canonical.Commit((transaction, _) => { transaction.PutRecord(KeyCodec.Encode(Obsolete), true); return true; });

    /// <summary>Reports whether snapshot replacement left any target-only state behind.</summary>
    public bool HasObsoleteRecord() => Canonical.Read(view => view.ReadOwnedValue(KeyCodec.Encode(Obsolete)) is not null);

    private void Bootstrap()
    {
        var principal = ReplicaCrashModel.PrincipalId;
        Database.Bootstrap(new(principal, System, [new(Wildcard, Wildcard, Capability.All)], [Wildcard]) { ClusterAdministrator = true },
            DatabaseEngine.Credential(principal, principal, Convert.ToHexString(RandomNumberGenerator.GetBytes(CredentialBytes))));
        RecoveryPhysicalShardBootstrap.Bootstrap(Database, principal, Configuration.VoterIds);
    }

    /// <summary>Disposes independently owned physical stores after all materializers have stopped.</summary>
    public void Dispose()
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(Log.Dispose, failures);
        try
        { ReplicaStore.Dispose(); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null) { failures.Add(error); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null) { failures.Add(error); }
        ServerFailureObserver.Observe(Canonical.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
