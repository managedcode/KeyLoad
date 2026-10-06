using KeyLoad.Replication;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class NodeEpochReplicaEmptySnapshotTests
{
    [Test]
    public async Task AcEpoch008EmptyReplicaConversionCreatesNoMetadataCommit()
    {
        using var fixture = new NodeEpochReplicaFixture(publishSnapshot: false);
        var replicaPosition = fixture.ReplicaStore.Position;
        var canonicalPosition = fixture.CanonicalStore.Position;
        var plan = fixture.Preflight();

        ReplicaSnapshotFormatUpgrade.Upgrade(plan, fixture.Database, fixture.ReplicaStore, UnitExecutionOptions.ReplicaConfiguration(            fixture.Configuration), fixture.DestinationSnapshots,             (_, _) => throw new InvalidOperationException("An empty snapshot inventory must not invoke conversion."), recoveryOptions: UnitExecutionOptions.OfflineRecovery(), executionOptions: UnitExecutionOptions.ReplicaExecution());

        await Assert.That(fixture.ReplicaStore.Position).IsEqualTo(replicaPosition);
        await Assert.That(fixture.CanonicalStore.Position).IsEqualTo(canonicalPosition);
        await Assert.That(Directory.Exists(fixture.DestinationSnapshots)).IsTrue();
        await Assert.That(Directory.EnumerateFileSystemEntries(fixture.DestinationSnapshots)).IsEmpty();
    }
}
