using KeyLoad.Replication;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class NodeEpochReplicaUnownedOutputTests
{
    private const string ConflictingOutput = "partial-converter-output";
    private static readonly byte[] OutputBytes = [0x61, 0x62];

    [Test]
    public async Task AcEpoch008FailedConverterDoesNotDeleteUnownedDestinationFile()
    {
        using var fixture = new NodeEpochReplicaFixture();
        var plan = fixture.Preflight();
        var replicaPosition = fixture.ReplicaStore.Position;
        var output = Path.Combine(fixture.DestinationSnapshots, ConflictingOutput);

        var failure = Assert.ThrowsExactly<AggregateException>(() => ReplicaSnapshotFormatUpgrade.Upgrade(
            plan, fixture.Database, fixture.ReplicaStore, fixture.Configuration,
            fixture.DestinationSnapshots, (_, _) =>
            {
                File.WriteAllBytes(output, OutputBytes);
                throw new IOException("Injected converter conflict.");
            }));

        await Assert.That(failure.InnerExceptions[0].Message).IsEqualTo("Injected converter conflict.");
        await Assert.That(await File.ReadAllBytesAsync(output)).IsEquivalentTo(OutputBytes);
        await Assert.That(fixture.ReplicaStore.Position).IsEqualTo(replicaPosition);
    }
}
