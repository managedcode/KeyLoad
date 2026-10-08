using KeyLoad.CrashHost;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.ClusterReplication;

internal static class ReplicaPrefixGcBoundaryAssertions
{
    internal const string DirectoryName = "prefix-boundary";
    private const string SavedImageSuffix = ".gc-negative-original";
    private const int CorruptFirstByte = 0;
    private const int FirstImageByte = 0;
    private const int CompleteInventoryRecords = 512;

    internal static async Task VerifyAsync(string root, Guid incarnation, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(root, DirectoryName);
        await ReplicaPrefixGcOwners.NodeAsync(directory, incarnation, async node =>
        {
            node.Populate(ReplicaPrefixGcCrashContract.SnapshotCut);
            var image = node.Snapshots.Create(ReplicaPrefixGcCrashContract.SnapshotCut, ReplicaPrefixGcCrashContract.InitialTerm);
            var before = Inventory(node.ReplicaStore);
            var canonical = ReplicaPrefixGcRecoveryOracle.CanonicalBytes(node);
            var position = node.ReplicaStore.Position;
            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync();
            try
            { _ = node.Snapshots.ReclaimCheckpointPrefix(canceled.Token); throw new InvalidOperationException("Canceled GC was admitted."); }
            catch (OperationCanceledException failure)
            { await Assert.That(failure.CancellationToken).IsEqualTo(canceled.Token); }
            await UnchangedAsync(node, before, canonical, position);
            await InvalidImageAsync(node, image, before, canonical, position, cancellationToken);
            await Assert.That(node.Snapshots.ReclaimCheckpointPrefix(cancellationToken)).IsEqualTo(ReplicaPrefixGcCrashContract.SnapshotCut);
            await Assert.That(ReplicaPrefixGcRecoveryOracle.PrefixCount(node)).IsEqualTo(FirstImageByte);
            await ReplicaPrefixGcReceiptAssertions.VerifyAsync(node, ReplicaPrefixGcCrashContract.SnapshotCut);
            ReplicaPrefixGcCrashScenario.AppendTail(node);
            await ReplicaPrefixGcReceiptAssertions.DocumentAsync(node, ReplicaPrefixGcCrashContract.TailCut);
            await ReplicaPrefixGcReceiptAssertions.VerifyAsync(node, ReplicaPrefixGcCrashContract.TailCut);
        });
        await ReplicaPrefixGcOwners.NodeAsync(directory, incarnation, async reopened =>
        {
            reopened.Snapshots.Recover();
            await ReplicaPrefixGcReceiptAssertions.DocumentAsync(reopened, ReplicaPrefixGcCrashContract.TailCut);
            await ReplicaPrefixGcReceiptAssertions.VerifyAsync(reopened, ReplicaPrefixGcCrashContract.SnapshotCut);
            await ReplicaPrefixGcReceiptAssertions.VerifyAsync(reopened, ReplicaPrefixGcCrashContract.TailCut);
        });
    }

    private static async Task InvalidImageAsync(ReplicaCrashNode node, KeyLoad.Replication.ReplicaSnapshot image,
        string[] before, string[] canonical, long position, CancellationToken cancellationToken)
    {
        var path = ReplicaProcessAssertions.ImagePath(node, image);
        var saved = path + SavedImageSuffix;
        var failures = new List<Exception>();
        var moved = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            File.Move(path, saved);
            moved = true;
            try
            { _ = node.Snapshots.ReclaimCheckpointPrefix(cancellationToken); throw new InvalidOperationException("Missing recovery image admitted GC."); }
            catch (FileNotFoundException) { }
            await UnchangedAsync(node, before, canonical, position);
            File.Copy(saved, path);
            using (var corrupt = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                corrupt.Position = FirstImageByte;
                corrupt.WriteByte(CorruptFirstByte);
                await corrupt.FlushAsync(cancellationToken);
                RandomAccess.FlushToDisk(corrupt.SafeFileHandle);
            }
            try
            { _ = node.Snapshots.ReclaimCheckpointPrefix(cancellationToken); throw new InvalidOperationException("Corrupt recovery image admitted GC."); }
            catch (KeyLoadException failure)
            { await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption); }
            await UnchangedAsync(node, before, canonical, position);
        }, failures);
        if (moved)
        {
            ServerFailureObserver.Observe(() => File.Delete(path), failures);
            ServerFailureObserver.Observe(() => File.Move(saved, path), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task UnchangedAsync(ReplicaCrashNode node, string[] before, string[] canonical, long position)
    {
        await Assert.That(node.ReplicaStore.Position).IsEqualTo(position);
        await Assert.That(Inventory(node.ReplicaStore).SequenceEqual(before, StringComparer.Ordinal)).IsTrue();
        await Assert.That(ReplicaPrefixGcRecoveryOracle.CanonicalBytes(node).SequenceEqual(canonical, StringComparer.Ordinal)).IsTrue();
        await Assert.That(ReplicaPrefixGcRecoveryOracle.PrefixCount(node)).IsEqualTo(ReplicaPrefixGcCrashContract.SnapshotCut);
    }

    private static string[] Inventory(ZoneTreeStore store)
    {
        var page = store.Read(view => view.Scan([], CompleteInventoryRecords));
        if (page.HasMore)
        { throw new InvalidOperationException("The native GC boundary fixture inventory is incomplete."); }
        return page.Records.Select(record => Convert.ToHexString(record.Key.Span) + ":" + Convert.ToHexString(record.Value.Span)).ToArray();
    }
}
