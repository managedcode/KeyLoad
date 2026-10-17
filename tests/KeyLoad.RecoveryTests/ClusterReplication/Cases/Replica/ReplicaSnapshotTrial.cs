using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.CrashHost;
using KeyLoad.Replication;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

internal sealed class ReplicaSnapshotTrial : IDisposable
{
    internal const string Interrupted = "interrupted-at-replica-boundary";
    internal const string ValueKey = "value";
    internal const string OldValue = "old-value";
    internal const string NewValue = "new-value";
    internal const string SystemKey = "system";
    internal const string AppliedKey = "last-applied";
    internal const string GuidFormat = "N";
    internal const string UnsafePath = "../foreign.snapshot";
    private const string Prefix = "keyload-replica-transfer-";
    private const string SourceDirectory = "source";
    private const string TargetDirectory = "target";
    private const string SourceLogDirectory = "source-log";
    private const string TargetLogDirectory = "target-log";
    private const string VoterA = "a";
    private const string VoterB = "b";
    private const string VoterC = "c";
    private readonly string directory = ReplicaFixturePaths.NewDirectory(Prefix);
    private readonly ZoneTreeStore source;
    private readonly ZoneTreeStore sourceLogStore;
    private readonly ZoneTreeStore targetLogStore;
    private readonly DurableReplicaLog sourceLog;
    internal ReplicaConfiguration Configuration
    {
        get;
    }
    internal ZoneTreeStore Target
    {
        get;
    }
    internal DurableReplicaLog TargetLog
    {
        get;
    }
    internal ReplicaSnapshotStore Sender
    {
        get;
    }
    internal ReplicaSnapshot Image
    {
        get;
    }
    internal string ImagePath => Path.Combine(directory, SourceDirectory, ReplicaProtocol.SnapshotDirectory, Image.FileName);
    internal string TargetImagePath(ReplicaSnapshot image) => Path.Combine(directory, TargetDirectory, ReplicaProtocol.SnapshotDirectory, image.FileName);

    internal ReplicaSnapshotTrial()
    {
        Configuration = new(VoterA, [VoterA, VoterB, VoterC], directory, Guid.NewGuid());
        source = Store(SourceDirectory);
        Target = Store(TargetDirectory);
        sourceLogStore = Store(SourceLogDirectory);
        targetLogStore = Store(TargetLogDirectory);
        sourceLog = new(sourceLogStore, RecoveryExecutionOptions.Configuration(Configuration));
        TargetLog = new(targetLogStore, RecoveryExecutionOptions.Configuration(Configuration));
        sourceLog.SaveTermAndVote(1, null);
        sourceLog.Append([new(1, 1, null)]);
        sourceLog.Commit(1);
        source.Commit((tx, _) =>
        {
            tx.PutRecord(KeyCodec.Encode(SystemKey, AppliedKey), 1L);
            tx.PutRecord(KeyCodec.Encode(ValueKey), NewValue);
            return true;
        });
        Target.Commit((tx, _) => { tx.PutRecord(KeyCodec.Encode(ValueKey), OldValue); return true; });
        Sender = new(source, sourceLog, RecoveryExecutionOptions.Configuration(Configuration with
        {
            Directory = Path.Combine(directory, SourceDirectory)
        }), RecoveryExecutionOptions.Replica(), replacementToken => RequireReplacement(source, replacementToken));
        Image = Sender.Create(1, 1);
    }

    private ZoneTreeStore Store(string name) => new(new(Path.Combine(directory, name)) { Incarnation = Configuration.Incarnation }, RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
    internal ReplicaSnapshotStore Receiver(Action<ReplicaCrashBoundary>? observer = null)
        => new(Target, TargetLog, RecoveryExecutionOptions.Configuration(Configuration with
        {
            Directory = Path.Combine(directory, TargetDirectory)
        }), RecoveryExecutionOptions.Replica(), replacementToken => RequireReplacement(Target, replacementToken), observer);

    private static void RequireReplacement(IAtomicStore store, CancellationToken cancellationToken)
    {
        var limits = RecoveryExecutionOptions.DatabaseLimits();
        ResourceRetirementSnapshotReplacement.Require(store, limits.Value,
            new ReadExecutionBudget(limits, TimeProvider.System, cancellationToken));
    }

    internal void Transfer(ReplicaSnapshotStore receiver, ReplicaSnapshot? image = null)
    {
        var snapshot = image ?? Image;
        var offset = receiver.Begin(snapshot);
        while (offset < snapshot.Length)
        {
            offset = receiver.Append(snapshot.TransferId, offset, Sender.ReadChunk(snapshot.TransferId, offset, Configuration.SnapshotChunkBytes));
        }
    }

    public void Dispose()
    {
        TargetLog.Dispose();
        sourceLog.Dispose();
        targetLogStore.Dispose();
        sourceLogStore.Dispose();
        Target.Dispose();
        source.Dispose();
        Directory.Delete(directory, true);
    }
}
