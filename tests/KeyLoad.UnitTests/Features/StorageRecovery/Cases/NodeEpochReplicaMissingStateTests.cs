using KeyLoad.Replication;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class NodeEpochReplicaMissingStateTests
{
    [Test]
    public async Task AcEpoch008MissingHardStateIsRejectedWithoutInitialization()
    {
        using var fixture = new NodeEpochReplicaFixture(publishSnapshot: false);
        fixture.ReplicaStore.Commit((transaction, _) =>
        {
            transaction.Delete(ReplicaProtocol.StateStorageKey);
            return true;
        });
        var position = fixture.ReplicaStore.Position;

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Preflight());

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(fixture.ReplicaStore.Position).IsEqualTo(position);
        await Assert.That(fixture.ReplicaStore.Read(view => view.ReadOwnedValue(ReplicaProtocol.StateStorageKey))).IsNull();
    }
}
