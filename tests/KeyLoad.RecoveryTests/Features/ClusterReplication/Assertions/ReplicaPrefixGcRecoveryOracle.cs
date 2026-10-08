using KeyLoad.CrashHost;
using KeyLoad.Replication;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.ClusterReplication;

internal static class ReplicaPrefixGcRecoveryOracle
{
    private static readonly Guid Incarnation = new("4c00fd96-12a5-4d82-b295-85bdd3432d0e");
    private const int FirstEntry = 1;
    private const int NoPrefix = 0;
    private const long SurvivingReplicaRecords = 3;
    private const long NoCanonicalApplyRecord = 0;
    private const string ReplicaDirectory = "replica";
    private const string JournalFile = "commands.wal";

    internal static async Task VerifyAsync(string root, CommitStage stage, CancellationToken cancellationToken)
    {
        var original = await ReplicaPrefixGcOwners.NodeAsync(root, Incarnation, async node =>
        {
            node.Snapshots.Recover();
            var image = node.Snapshots.Current!;
            await Assert.That(image.Index).IsEqualTo(ReplicaPrefixGcCrashContract.SnapshotCut);
            await Assert.That(image.Term).IsEqualTo(ReplicaPrefixGcCrashContract.InitialTerm);
            var remaining = PrefixCount(node);
            await Assert.That(remaining == NoPrefix || remaining == ReplicaPrefixGcCrashContract.SnapshotCut).IsTrue();
            if (stage is not (CommitStage.HeaderWritten or CommitStage.PayloadWritten))
            { await Assert.That(remaining).IsEqualTo(NoPrefix); }
            var canonical = CanonicalBytes(node);
            var hardstate = ReplicaProtocolCodec.Serialize(node.Log.State);
            await ReplicaPrefixGcReceiptAssertions.DocumentAsync(node, ReplicaPrefixGcCrashContract.TailCut);
            await ReplicaPrefixGcReceiptAssertions.AllOriginalAsync(node, ReplicaPrefixGcCrashContract.TailCut);
            await Assert.That(node.Snapshots.ReclaimCheckpointPrefix(cancellationToken)).IsEqualTo(remaining);
            await Assert.That(PrefixCount(node)).IsEqualTo(NoPrefix);
            var rewritten = node.ReplicaStore.VerifySnapshot(Path.Combine(node.Configuration.Directory, ReplicaDirectory, JournalFile));
            await Assert.That(rewritten.RecordCount).IsEqualTo(SurvivingReplicaRecords);
            await Assert.That(rewritten.AppliedPosition).IsEqualTo(NoCanonicalApplyRecord);
            await Assert.That(rewritten.Position).IsEqualTo(node.ReplicaStore.Position);
            await Assert.That(rewritten.Incarnation).IsEqualTo(Incarnation);
            await Assert.That(ReplicaProtocolCodec.Serialize(node.Log.State).AsSpan().SequenceEqual(hardstate)).IsTrue();
            await Assert.That(CanonicalBytes(node).SequenceEqual(canonical, StringComparer.Ordinal)).IsTrue();
            await Assert.That(node.Log.TermAt(ReplicaPrefixGcCrashContract.SnapshotCut)).IsEqualTo(ReplicaPrefixGcCrashContract.InitialTerm);
            await Assert.That(node.Snapshots.ReclaimCheckpointPrefix(cancellationToken)).IsEqualTo(NoPrefix);
            return (Image: image, Canonical: canonical);
        });
        await ReplicaPrefixGcOwners.NodeAsync(root, Incarnation, async reopened =>
        {
            reopened.Snapshots.Recover();
            await Assert.That(PrefixCount(reopened)).IsEqualTo(NoPrefix);
            await Assert.That(CanonicalBytes(reopened).SequenceEqual(original.Canonical, StringComparer.Ordinal)).IsTrue();
            await ReplicaPrefixGcFreshTarget.VerifyAsync(root, reopened, original.Image, cancellationToken);
            await ReplicaPrefixGcBoundaryAssertions.VerifyAsync(root, Incarnation, cancellationToken);
        });
    }

    internal static int PrefixCount(ReplicaCrashNode node)
        => Enumerable.Range(FirstEntry, ReplicaPrefixGcCrashContract.SnapshotCut).Count(index =>
            node.ReplicaStore.Read(view => view.ReadOwnedValue(ReplicaProtocol.EntryStorageKey(index))) is not null);

    internal static string[] CanonicalBytes(ReplicaCrashNode node)
    {
        const int NativeFixtureRecordLimit = 512;
        var page = node.Canonical.Read(view => view.Scan([], NativeFixtureRecordLimit));
        if (page.HasMore)
        { throw new InvalidOperationException("The actual canonical GC fixture exceeded its complete native inventory cap."); }
        return page.Records.Select(record => Convert.ToHexString(record.Key.Span) + ":" +
            Convert.ToHexString(record.Value.Span)).ToArray();
    }
}
