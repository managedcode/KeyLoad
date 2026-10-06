using KeyLoad.Replication;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class NodeEpochReplicaBindingTests
{
    [Test]
    public async Task AcEpoch008ChangedSourceIdentityRejectsPlanBeforeCreatingDestination()
    {
        using var fixture = new NodeEpochReplicaFixture();
        var plan = fixture.Preflight();
        var replicaPosition = fixture.ReplicaStore.Position;
        fixture.CanonicalStore.SetDispatchPaused(true);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => ReplicaSnapshotFormatUpgrade.Upgrade(            plan, fixture.Database, fixture.ReplicaStore, UnitExecutionOptions.ReplicaConfiguration(fixture.Configuration),             fixture.DestinationSnapshots, fixture.Convert, recoveryOptions: UnitExecutionOptions.OfflineRecovery(), executionOptions: UnitExecutionOptions.ReplicaExecution()));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(Directory.Exists(fixture.DestinationSnapshots)).IsFalse();
        await Assert.That(fixture.ReplicaStore.Position).IsEqualTo(replicaPosition);
    }
}
