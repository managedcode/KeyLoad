using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

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
    private readonly ZoneTreeStore replicaStore;

    private ReplicaCrashNode(string directory, string voter, Guid incarnation, Action<ReplicaCrashBoundary>? observer)
    {
        Configuration = new(voter, [VoterA, VoterB, VoterC], directory, incarnation);
        Canonical = new(new(Path.Combine(directory, CanonicalDirectory)) { Incarnation = incarnation }, CrashExecutionOptions.StorageExecution(), CrashExecutionOptions.PointCacheExecution());
        try
        {
            replicaStore = new(new(Path.Combine(directory, ReplicaDirectory)) { Incarnation = incarnation }, CrashExecutionOptions.StorageExecution(), CrashExecutionOptions.PointCacheExecution());
            try
            {
                Database = new(Canonical, new AuthorizationPolicy(), CrashExecutionOptions.DatabaseLimits(), CrashExecutionOptions.DueWork(), CrashExecutionOptions.EventSource(), CrashExecutionOptions.Messaging(), CrashExecutionOptions.GraphExecution(), CrashExecutionOptions.ChangeFeedExecution(), CrashExecutionOptions.TimeSeriesExecution());
                Log = new(replicaStore, CrashExecutionOptions.Configuration(Configuration), observer, canonicalDatabase: Database);
                Bootstrap();
                Snapshots = new(Canonical, Log, CrashExecutionOptions.Configuration(Configuration), CrashExecutionOptions.Replica(), observer);
            }
            catch (Exception)
            {
                replicaStore.Dispose();
                throw;
            }
        }
        catch (Exception)
        {
            Canonical.Dispose();
            throw;
        }
    }

    /// <summary>The configured fixed RF3 voter set and stable cluster incarnation.</summary>
    public ReplicaConfiguration Configuration { get; }
    /// <summary>The node-owned canonical storage engine.</summary>
    public ZoneTreeStore Canonical { get; }
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
    public static ReplicaCrashNode OpenTarget(string root, Guid incarnation, Action<ReplicaCrashBoundary>? observer = null)
        => new(Path.Combine(root, TargetDirectory), VoterA, incarnation, observer);

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
        Log.SaveTermAndVote(1, null);
        for (var index = 1; index <= cut; index++)
        {
            var operation = Database.NormalizeOperation(ReplicaCrashModel.Operation(index));
            Log.Append([new(index, 1, operation)]);
            Log.Commit(index);
            var result = Database.Apply(operation, index);
            if (index == 1)
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
        Log.Dispose();
        replicaStore.Dispose();
        Canonical.Dispose();
    }
}
