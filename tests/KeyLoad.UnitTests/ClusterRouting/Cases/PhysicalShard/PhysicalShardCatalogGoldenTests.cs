
namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PhysicalShardCatalogGoldenTests
{
    [Test]
    public async Task AcScat002NativeContractsAndBootstrapCommandIdMatchFrozenGoldenVector()
    {
        var shardId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var commandId = PhysicalShardCatalogIdentity.CreateBootstrapCommandId(shardId);
        await Assert.That(commandId).IsEqualTo(Guid.Parse("749d23fe-8e00-a5c2-4b6b-aee7e50adff7"));
        var emptyShardFailure = Assert.ThrowsExactly<ArgumentException>(
            () => PhysicalShardCatalogIdentity.CreateBootstrapCommandId(Guid.Empty));
        await Assert.That(emptyShardFailure.ParamName).IsEqualTo("physicalShardId");
        var request = new BootstrapPhysicalShardCatalogRequest(1, 0, shardId,
            Guid.Parse("ffeeddcc-bbaa-9988-7766-554433221100"), [PhysicalShardCatalogVoterIds.First]);
        var restored = NativeSerialization.Deserialize<BootstrapPhysicalShardCatalogRequest>(
            NativeSerialization.Serialize(request));
        await Assert.That(restored.Version).IsEqualTo(request.Version);
        await Assert.That(restored.ExpectedRevision).IsEqualTo(request.ExpectedRevision);
        await Assert.That(restored.PhysicalShardId).IsEqualTo(request.PhysicalShardId);
        await Assert.That(restored.Incarnation).IsEqualTo(request.Incarnation);
        await Assert.That(restored.VoterIds.SequenceEqual(request.VoterIds, StringComparer.Ordinal)).IsTrue();
        var catalog = new PhysicalShardCatalog(1, 1,
            new PhysicalShardRecord(shardId, request.Incarnation, request.VoterIds, 1));
        var restoredCatalog = NativeSerialization.Deserialize<PhysicalShardCatalog>(NativeSerialization.Serialize(catalog));
        await Assert.That(restoredCatalog.Version).IsEqualTo(catalog.Version);
        await Assert.That(restoredCatalog.Revision).IsEqualTo(catalog.Revision);
        await Assert.That(restoredCatalog.DefaultShard.PhysicalShardId)
            .IsEqualTo(catalog.DefaultShard.PhysicalShardId);
        await Assert.That(restoredCatalog.DefaultShard.Incarnation)
            .IsEqualTo(catalog.DefaultShard.Incarnation);
        await Assert.That(restoredCatalog.DefaultShard.VoterIds.SequenceEqual(
            catalog.DefaultShard.VoterIds, StringComparer.Ordinal)).IsTrue();
        await Assert.That(restoredCatalog.DefaultShard.PlacementEpoch).IsEqualTo(catalog.DefaultShard.PlacementEpoch);
    }
}
