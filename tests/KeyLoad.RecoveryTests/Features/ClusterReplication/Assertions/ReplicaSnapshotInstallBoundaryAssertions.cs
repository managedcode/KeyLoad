using System.Security.Cryptography;
using KeyLoad.CrashHost;
using KeyLoad.Replication;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.ClusterReplication;

internal static class ReplicaSnapshotInstallBoundaryAssertions
{
    private const int CompleteNativeRecordLimit = 512;

    internal static (string[] Canonical, long CanonicalPosition, string[] Replica, long ReplicaPosition,
        byte[] HardState, Guid NodeId, long ReadGeneration, (long Length, string Hash) Image,
        (long Length, string Hash) Manifest) Capture(ReplicaCrashNode node)
        => (Inventory(node.Canonical), node.Canonical.Position, Inventory(node.ReplicaStore), node.ReplicaStore.Position,
            ReplicaProtocolCodec.Serialize(node.Log.State), node.Canonical.Identity.NodeId,
            node.Canonical.Identity.ReadGeneration, FileDigest(node, ReplicaProtocol.IncomingImage),
            FileDigest(node, ReplicaProtocol.IncomingManifest));

    internal static async Task UnchangedAsync(ReplicaCrashNode node,
        (string[] Canonical, long CanonicalPosition, string[] Replica, long ReplicaPosition,
            byte[] HardState, Guid NodeId, long ReadGeneration, (long Length, string Hash) Image,
            (long Length, string Hash) Manifest) before)
    {
        var after = Capture(node);
        await Assert.That(after.Canonical.SequenceEqual(before.Canonical, StringComparer.Ordinal)).IsTrue();
        await Assert.That(after.CanonicalPosition).IsEqualTo(before.CanonicalPosition);
        await Assert.That(after.Replica.SequenceEqual(before.Replica, StringComparer.Ordinal)).IsTrue();
        await Assert.That(after.ReplicaPosition).IsEqualTo(before.ReplicaPosition);
        await Assert.That(after.HardState.AsSpan().SequenceEqual(before.HardState)).IsTrue();
        await Assert.That(after.NodeId).IsEqualTo(before.NodeId);
        await Assert.That(after.ReadGeneration).IsEqualTo(before.ReadGeneration);
        await Assert.That(after.Image).IsEqualTo(before.Image);
        await Assert.That(after.Manifest).IsEqualTo(before.Manifest);
        await Assert.That(node.Snapshots.Current).IsNull();
    }

    private static string[] Inventory(ZoneTreeStore store)
    {
        var page = store.Read(view => view.Scan([], CompleteNativeRecordLimit));
        if (page.HasMore) { throw new InvalidOperationException(); }
        return page.Records.Select(record => Convert.ToHexString(record.Key.Span) + ":" +
            Convert.ToHexString(record.Value.Span)).ToArray();
    }

    private static (long Length, string Hash) FileDigest(ReplicaCrashNode node, string name)
    {
        var path = Path.Combine(node.Configuration.Directory, ReplicaProtocol.SnapshotDirectory, name);
        using var file = File.OpenRead(path);
        return (file.Length, Convert.ToHexStringLower(SHA256.HashData(file)));
    }
}
