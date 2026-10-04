namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class NodeEpochReplicaPendingTransferTests
{
    private const string PendingManifest = "incoming.json";
    private static readonly byte[] PendingBytes = [0x71, 0x72, 0x73];

    [Test]
    public async Task AcEpoch008PreflightRefusesPendingTransferWithoutChangingAuthorityOrFiles()
    {
        using var fixture = new NodeEpochReplicaFixture();
        var pendingPath = Path.Combine(fixture.SourceSnapshots, PendingManifest);
        await File.WriteAllBytesAsync(pendingPath, PendingBytes);
        var replicaPosition = fixture.ReplicaStore.Position;
        var canonicalPosition = fixture.CanonicalStore.Position;

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Preflight());

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(fixture.ReplicaStore.Position).IsEqualTo(replicaPosition);
        await Assert.That(fixture.CanonicalStore.Position).IsEqualTo(canonicalPosition);
        await Assert.That(await File.ReadAllBytesAsync(pendingPath)).IsEquivalentTo(PendingBytes);
        await Assert.That(Directory.Exists(fixture.DestinationSnapshots)).IsFalse();
    }
}
