namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class NodeEpochReplicaUnknownInventoryTests
{
    private const string UnknownFile = "unowned.bin";
    private static readonly byte[] UnknownBytes = [0x11, 0x22];

    [Test]
    public async Task AcEpoch008UnknownSnapshotEntryFailsClosedAndPreservesEvidence()
    {
        using var fixture = new NodeEpochReplicaFixture();
        var path = Path.Combine(fixture.SourceSnapshots, UnknownFile);
        await File.WriteAllBytesAsync(path, UnknownBytes);
        var sourceImage = Path.Combine(fixture.SourceSnapshots, fixture.Pointer.FileName);
        var sourceBytes = await File.ReadAllBytesAsync(sourceImage);
        var replicaPosition = fixture.ReplicaStore.Position;

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Preflight());

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(await File.ReadAllBytesAsync(path)).IsEquivalentTo(UnknownBytes);
        await Assert.That(await File.ReadAllBytesAsync(sourceImage)).IsEquivalentTo(sourceBytes);
        await Assert.That(fixture.ReplicaStore.Position).IsEqualTo(replicaPosition);
    }
}
