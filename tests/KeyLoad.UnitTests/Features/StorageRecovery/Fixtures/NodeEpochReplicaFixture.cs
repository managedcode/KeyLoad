using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class NodeEpochReplicaFixture : IDisposable
{
    private const string Prefix = "keyload-node-epoch-replica-";
    private const string CanonicalDirectory = "canonical";
    private const string ReplicaDirectory = "replica-store";
    private const string DestinationDirectory = "destination-node";
    private const string SourceDirectory = "source-images";
    private const string CutProbeName = "canonical-cut-probe.snapshot";
    private const string VoterA = "node-a";
    private const string VoterB = "node-b";
    private const string VoterC = "node-c";
    private const string GuidFormat = "N";
    private const long SnapshotCut = 1;
    private const long SnapshotTerm = 1;
    internal const long FirstRetainedEntryIndex = SnapshotCut + 1;
    internal const int RetainedSuffixEntryCount = 2;
    private const int SigningKeyBytes = 32;
    private readonly string root = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString(GuidFormat));

    internal NodeEpochReplicaFixture(bool publishSnapshot = true)
    {
        Configuration = new(VoterA, [VoterA, VoterB, VoterC], Path.Combine(root, DestinationDirectory), Guid.NewGuid());
        Directory.CreateDirectory(root);
        CanonicalStore = Open(CanonicalDirectory);
        ReplicaStore = Open(ReplicaDirectory);
        Database = new(CanonicalStore, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution());
        if (publishSnapshot)
        { SourceCut = CreateSourceMetadataAndPointer(); }
        else
        {
            using var log = new DurableReplicaLog(ReplicaStore, UnitExecutionOptions.ReplicaConfiguration(Configuration), canonicalDatabase: Database);
            SourceCut = CaptureCurrentCut(Database.LastApplied);
        }
    }

    internal ReplicaConfiguration Configuration { get; }
    internal ZoneTreeStore CanonicalStore { get; }
    internal ZoneTreeStore ReplicaStore { get; }
    internal DatabaseEngine Database { get; }
    internal string SourceSnapshots => Path.Combine(root, SourceDirectory);
    internal string DestinationSnapshots => Path.Combine(Configuration.Directory, ReplicaProtocol.SnapshotDirectory);
    internal ReplicaSnapshot Pointer { get; private set; } = null!;
    internal StorageSnapshot SourceCut { get; private set; }
    // The injected verifier supplies SourceCut; these bytes deliberately do not model a stored snapshot format.
    internal byte[] SourceImageBytes { get; } = [0x53, 0x52, 0x43];

    internal ReplicaSnapshotUpgradePlan Preflight()
        => ReplicaSnapshotFormatUpgrade.Preflight(Database, ReplicaStore, UnitExecutionOptions.ReplicaConfiguration(Configuration), SourceSnapshots, _ => SourceCut, recoveryOptions: UnitExecutionOptions.OfflineRecovery(), executionOptions: UnitExecutionOptions.ReplicaExecution());

    internal StorageSnapshot Convert(string _, string destination)
        => CanonicalStore.CreateSnapshot(destination, SourceCut.AppliedPosition);

    internal void AddSourceImage(string fileName)
    {
        File.WriteAllBytes(Path.Combine(SourceSnapshots, fileName), SourceImageBytes);
    }

    internal byte[] ReadRetainedEntryBytes(long index)
        => ReplicaStore.Read(view => view.ReadOwnedValue(ReplicaProtocol.EntryStorageKey(index))
            ?? throw new InvalidDataException(ReplicaProtocol.CorruptLog));

    private ZoneTreeStore Open(string name)
        => new(new(Path.Combine(root, name))
        { Incarnation = Configuration.Incarnation, SigningKey = RandomNumberGenerator.GetBytes(SigningKeyBytes) }, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());

    private StorageSnapshot CreateSourceMetadataAndPointer()
    {
        Directory.CreateDirectory(SourceSnapshots);
        using var log = new DurableReplicaLog(ReplicaStore, UnitExecutionOptions.ReplicaConfiguration(Configuration), canonicalDatabase: Database);
        log.SaveTermAndVote(SnapshotTerm, null);
        log.Append([new ReplicaEntry(SnapshotCut, SnapshotTerm, null)]);
        log.Commit(SnapshotCut);
        ReplicaCanonicalApply.ApplyBatch(Database, log, batchSize: 1);
        var sourceCut = CaptureCurrentCut(SnapshotCut);
        var id = Guid.NewGuid();
        var fileName = id.ToString(GuidFormat) + ReplicaProtocol.SnapshotExtension;
        var path = Path.Combine(SourceSnapshots, fileName);
        File.WriteAllBytes(path, SourceImageBytes);
        Pointer = new(id, Configuration.Incarnation, SnapshotCut, SnapshotTerm, new FileInfo(path).Length,
            System.Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), fileName);
        log.PublishSnapshot(Pointer);
        log.Append(Enumerable.Range(0, RetainedSuffixEntryCount).Select(offset =>
            new ReplicaEntry(FirstRetainedEntryIndex + offset, SnapshotTerm, null)).ToArray());
        return sourceCut;
    }

    private StorageSnapshot CaptureCurrentCut(long appliedPosition)
    {
        var probe = Path.Combine(root, CutProbeName);
        var snapshot = CanonicalStore.CreateSnapshot(probe, appliedPosition);
        File.Delete(probe);
        return snapshot;
    }

    public void Dispose()
    {
        try
        { ReplicaStore.Dispose(); }
        finally
        {
            try
            { CanonicalStore.Dispose(); }
            finally
            {
                if (Directory.Exists(root))
                { Directory.Delete(root, true); }
            }
        }
    }
}
