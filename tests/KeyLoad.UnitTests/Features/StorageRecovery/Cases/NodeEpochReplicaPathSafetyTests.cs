using KeyLoad.Replication;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class NodeEpochReplicaPathSafetyTests
{
    private const string LinkedSourceName = "source-images-link";

    [Test]
    public async Task AcEpoch008SnapshotSourceAndDestinationAliasIsRejectedBeforeStoreAccess()
    {
        using var fixture = new NodeEpochReplicaFixture();
        var replicaPosition = fixture.ReplicaStore.Position;
        var canonicalPosition = fixture.CanonicalStore.Position;

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => ReplicaSnapshotFormatUpgrade.Preflight(
            fixture.Database, fixture.ReplicaStore, fixture.Configuration, fixture.DestinationSnapshots,
            _ => fixture.SourceCut));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(fixture.ReplicaStore.Position).IsEqualTo(replicaPosition);
        await Assert.That(fixture.CanonicalStore.Position).IsEqualTo(canonicalPosition);
        await Assert.That(Directory.Exists(fixture.DestinationSnapshots)).IsFalse();
    }

    [Test]
    public async Task AcEpoch008LinkedSnapshotSourceIsRejectedWithoutFollowingIt()
    {
        using var fixture = new NodeEpochReplicaFixture();
        var linked = Path.Combine(Path.GetDirectoryName(fixture.SourceSnapshots)!, LinkedSourceName);
        Directory.CreateSymbolicLink(linked, fixture.SourceSnapshots);
        var replicaPosition = fixture.ReplicaStore.Position;

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => ReplicaSnapshotFormatUpgrade.Preflight(
            fixture.Database, fixture.ReplicaStore, fixture.Configuration, linked, _ => fixture.SourceCut));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(fixture.ReplicaStore.Position).IsEqualTo(replicaPosition);
        await Assert.That(new DirectoryInfo(linked).LinkTarget).IsEqualTo(fixture.SourceSnapshots);
    }
}
