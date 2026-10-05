using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Security;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Logging;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal enum PendingPrincipal { Missing, Modified, Valid }

internal sealed class VerifiedSnapshotInterruption : Exception
{
    public VerifiedSnapshotInterruption() { }
    public VerifiedSnapshotInterruption(string message) : base(message) { }
    public VerifiedSnapshotInterruption(string message, Exception innerException) : base(message, innerException) { }
}

internal sealed class PartitionHostRecoveryFixture : IDisposable
{
    internal const long SnapshotCut = 2;
    internal const long SnapshotTerm = 1;
    internal const string CanonicalDirectory = "database";
    internal const string OwnershipFile = "node.owner.lock";
    private const string Prefix = "keyload-host-recovery-";
    private const string SourceDirectory = "source";
    private const string TargetDirectory = "target";
    private const string GuidFormat = "N";
    private const string AdminPrefix = "root.";
    private const string VoterA = "https://127.0.0.1:5501";
    private const string VoterB = "https://127.0.0.1:5502";
    private const string VoterC = "https://127.0.0.1:5503";
    private const int SecretBytes = 32;
    private readonly string directory;
    private readonly ILoggerFactory logging = LoggerFactory.Create(_ => { });

    internal PartitionHostRecoveryFixture()
    {
        directory = Path.Combine(Resolve(new(Path.GetTempPath())), Prefix + Guid.NewGuid().ToString(GuidFormat));
        Options = new()
        {
            DataDirectory = Path.Combine(directory, TargetDirectory),
            PublicEndpoint = VoterA,
            Peers = [VoterA, VoterB, VoterC],
            PhysicalShardId = Guid.NewGuid(),
            Incarnation = Guid.NewGuid(),
            SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes)),
            PeerSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes)),
            AdminKey = AdminPrefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(SecretBytes))
        };
        Options.Validate();
    }

    internal NodeOptions Options { get; }
    internal ReplicaSnapshot Pending { get; private set; } = null!;
    internal Guid TargetNodeId { get; private set; }
    internal long TargetGeneration { get; private set; }

    internal void StagePending(PendingPrincipal principal)
    {
        using var source = new HostReplicaStores(Options with
        { DataDirectory = Path.Combine(directory, SourceDirectory), PublicEndpoint = VoterB });
        using var target = OpenStores();
        TargetNodeId = target.Canonical.Identity.NodeId;
        TargetGeneration = target.Canonical.Identity.ReadGeneration;
        ClusterPrincipalPolicy.Initialize(source.Database);
        source.Canonical.Commit((transaction, _) =>
        {
            var key = KeySpace.Principal(ClusterPrincipalPolicy.InternalPrincipalId);
            if (principal == PendingPrincipal.Missing)
            { transaction.Delete(key); }
            else if (principal == PendingPrincipal.Modified)
            {
                var original = transaction.GetRecord<PrincipalRecord>(key)!;
                transaction.PutRecord(key, original with { Revoked = true });
            }
            transaction.PutRecord(KeySpace.Applied.ToArray(), SnapshotCut);
            return true;
        });
        source.Log.SaveTermAndVote(SnapshotTerm, null);
        source.Log.Append([new(1, SnapshotTerm, null), new(SnapshotCut, SnapshotTerm, null)]);
        source.Log.Commit(SnapshotCut);
        Pending = source.Snapshots.Create(SnapshotCut, SnapshotTerm);
        var incoming = new ReplicaSnapshotStore(target.Canonical, target.Log, UnitExecutionOptions.ReplicaConfiguration(target.Configuration), UnitExecutionOptions.ReplicaExecution(), boundary =>
        {
            if (boundary == ReplicaCrashBoundary.SnapshotVerified)
            { throw new VerifiedSnapshotInterruption(); }
        });
        var offset = incoming.Begin(Pending);
        while (offset < Pending.Length)
        {
            var bytes = source.Snapshots.ReadChunk(Pending.TransferId, offset, source.Configuration.SnapshotChunkBytes);
            offset = incoming.Append(Pending.TransferId, offset, bytes);
        }
        incoming.Complete(Pending.TransferId);
    }

    internal PartitionHost OpenHost() => new(ServerRuntimeTestOptions.Runtime(Options), new AuthorizationPolicy(),         new CommandAdmissionGovernor(UnitAdmissionOptions.Command(Options.CommandAdmission)), TimeProvider.System, logging.CreateLogger<ReplicaConsensus>());

    internal async Task OpenAndDisposeHostAsync()
    {
        await using var host = OpenHost();
    }

    internal HostReplicaStores OpenStores() => new(Options);

    internal FileStream AcquireHostOwnership() => new(Path.Combine(Options.DataDirectory, OwnershipFile),
        FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    private static string Resolve(DirectoryInfo directory)
    {
        if (directory.Parent is null)
        { return directory.FullName; }
        var entry = new DirectoryInfo(Path.Combine(Resolve(directory.Parent), directory.Name));
        return entry.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? entry.FullName;
    }

    public void Dispose()
    {
        logging.Dispose();
        if (Directory.Exists(directory))
        { Directory.Delete(directory, true); }
    }
}

internal sealed class HostReplicaStores : IDisposable
{
    private readonly ZoneTreeStore replica;

    internal HostReplicaStores(NodeOptions options)
    {
        Configuration = options.CreateReplicaConfiguration(options.DataDirectory);
        Canonical = Open(options, PartitionHostRecoveryFixture.CanonicalDirectory);
        try
        {
            replica = Open(options, ReplicaProtocol.ReplicaDirectory);
            try
            {
                Log = new(replica, UnitExecutionOptions.ReplicaConfiguration(Configuration));
                Database = new(Canonical, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource());
                Snapshots = new(Canonical, Log, UnitExecutionOptions.ReplicaConfiguration(Configuration), UnitExecutionOptions.ReplicaExecution());
            }
            catch (Exception) { replica.Dispose(); throw; }
        }
        catch (Exception) { Canonical.Dispose(); throw; }
    }

    internal ReplicaConfiguration Configuration { get; }
    internal ZoneTreeStore Canonical { get; }
    internal DurableReplicaLog Log { get; }
    internal DatabaseEngine Database { get; }
    internal ReplicaSnapshotStore Snapshots { get; }

    private static ZoneTreeStore Open(NodeOptions options, string suffix) => new(new(Path.Combine(options.DataDirectory, suffix))
    { Incarnation = options.Incarnation, SigningKey = Convert.FromBase64String(options.SigningKey) }, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());

    public void Dispose()
    {
        try
        { Log.Dispose(); }
        finally
        {
            try
            { replica.Dispose(); }
            finally { Canonical.Dispose(); }
        }
    }
}
