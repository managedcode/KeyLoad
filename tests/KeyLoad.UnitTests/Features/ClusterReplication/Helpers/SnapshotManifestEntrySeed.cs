using KeyLoad.CrashHost;
using KeyLoad.Replication;
using KeyLoad.Server;
using P = KeyLoad.UnitTests.Features.ClusterReplication.SnapshotManifestEntryProtocol;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class SnapshotManifestEntrySeed
{
    internal static async Task<SnapshotManifestEntryState> RunAsync(SnapshotManifestEntryOwners owner, CancellationToken token)
    {
        var source = owner.Open(source: true);
        var target = owner.Open(source: false);
        source.Populate(P.SnapshotCut);
        var image = source.Snapshots.Create(P.SnapshotCut, P.Term);
        target.Populate(P.OriginalCut);
        target.AddObsoleteRecord();
        target.Log.Append([new(P.SnapshotCut, P.Term, target.Database.NormalizeOperation(ReplicaCrashModel.Operation(P.SnapshotCut))),
            new(P.TailCut, P.Term, target.Database.NormalizeOperation(ReplicaCrashModel.Operation(P.TailCut)))]);
        await Assert.That(target.Snapshots.Begin(image)).IsEqualTo(0L);
        var prefix = source.Snapshots.ReadChunk(image.TransferId, 0, ReplicaCrashTransfer.PrefixBytes);
        await Assert.That(target.Snapshots.Append(image.TransferId, 0, prefix)).IsEqualTo((long)prefix.Length);
        var directory = Path.Combine(target.Configuration.Directory, ReplicaProtocol.SnapshotDirectory);
        var manifestPath = Path.Combine(directory, ReplicaProtocol.IncomingManifest);
        var prefixPath = Path.Combine(directory, ReplicaProtocol.IncomingImage);
        await SnapshotManifestEntryAssertions.DocumentAsync(target, P.OriginalCut);
        var sourceImagePath = Path.Combine(source.Configuration.Directory, ReplicaProtocol.SnapshotDirectory, image.FileName);
        var state = new SnapshotManifestEntryState(image, manifestPath, prefixPath,
            await File.ReadAllBytesAsync(manifestPath, token), prefix, sourceImagePath, await File.ReadAllBytesAsync(sourceImagePath, token),
            SnapshotManifestEntryAssertions.Bytes(target.Canonical), target.Canonical.Position,
            SnapshotManifestEntryAssertions.Bytes(target.ReplicaStore), target.ReplicaStore.Position,
            ReplicaProtocolCodec.Serialize(target.Log.State), target.Canonical.Identity);
        var failures = new List<Exception>();
        await owner.CloseAsync(failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return state;
    }

    internal static async Task UnchangedAsync(SnapshotManifestEntryOwners owner, SnapshotManifestEntryState state, CancellationToken token)
    {
        var node = owner.Open(source: false);
        await Assert.That(SnapshotManifestEntryAssertions.Bytes(node.Canonical).SequenceEqual(state.Canonical, StringComparer.Ordinal)).IsTrue();
        await Assert.That(node.Canonical.Position).IsEqualTo(state.CanonicalPosition);
        await Assert.That(SnapshotManifestEntryAssertions.Bytes(node.ReplicaStore).SequenceEqual(state.Replica, StringComparer.Ordinal)).IsTrue();
        await Assert.That(node.ReplicaStore.Position).IsEqualTo(state.ReplicaPosition);
        await Assert.That(ReplicaProtocolCodec.Serialize(node.Log.State).AsSpan().SequenceEqual(state.HardState)).IsTrue();
        await Assert.That(node.Canonical.Identity.NodeId).IsEqualTo(state.Identity.NodeId);
        await Assert.That(node.Canonical.Identity.Incarnation).IsEqualTo(state.Identity.Incarnation);
        await Assert.That(node.Canonical.Identity.ReadGeneration).IsEqualTo(state.Identity.ReadGeneration);
        await Assert.That((await File.ReadAllBytesAsync(state.PrefixPath, token)).AsSpan().SequenceEqual(state.Prefix)).IsTrue();
        await SnapshotManifestEntryAssertions.DocumentAsync(node, P.OriginalCut);
        var failures = new List<Exception>();
        await owner.CloseAsync(failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
