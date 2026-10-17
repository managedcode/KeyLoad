using KeyLoad.Replication;
using KeyLoad.Server;
using P = KeyLoad.UnitTests.Features.ClusterReplication.SnapshotManifestEntryProtocol;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class SnapshotManifestEntryCold
{
    internal static async Task RunAsync(SnapshotManifestEntryOwners owner, SnapshotManifestEntryTerminal terminal, CancellationToken token)
    {
        var failures = new List<Exception>();
        for (var cycle = 0; cycle < P.ColdCount && owner.CanOpen; cycle++)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var node = owner.Open(source: false);
                var apply = owner.StartMaterializer();
                await apply.WaitForApplyAsync(P.HealthyCut, token);
                await Assert.That(node.Canonical.Identity.Incarnation).IsEqualTo(owner.Incarnation);
                await Assert.That(node.Canonical.Identity.NodeId).IsEqualTo(terminal.Identity.NodeId);
                await Assert.That(node.Canonical.Identity.ReadGeneration).IsGreaterThanOrEqualTo(terminal.Identity.ReadGeneration);
                await Assert.That(SnapshotManifestEntryAssertions.Bytes(node.Canonical).SequenceEqual(terminal.Canonical, StringComparer.Ordinal)).IsTrue();
                await Assert.That(node.Canonical.Position).IsEqualTo(terminal.CanonicalPosition);
                await Assert.That(SnapshotManifestEntryAssertions.Bytes(node.ReplicaStore).SequenceEqual(terminal.Replica, StringComparer.Ordinal)).IsTrue();
                await Assert.That(node.ReplicaStore.Position).IsEqualTo(terminal.ReplicaPosition);
                await Assert.That(ReplicaProtocolCodec.Serialize(node.Log.State).AsSpan().SequenceEqual(terminal.HardState)).IsTrue();
                await SnapshotManifestEntryAssertions.DocumentAsync(node, P.HealthyCut);
                await SnapshotManifestEntryAssertions.AllReceiptsAsync(node, terminal.Healthy);
                await Assert.That(SnapshotManifestEntryAssertions.Bytes(node.Canonical).SequenceEqual(terminal.Canonical, StringComparer.Ordinal)).IsTrue();
                await Assert.That(node.Canonical.Position).IsEqualTo(terminal.CanonicalPosition);
            }, failures);
            await owner.CloseAsync(failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
