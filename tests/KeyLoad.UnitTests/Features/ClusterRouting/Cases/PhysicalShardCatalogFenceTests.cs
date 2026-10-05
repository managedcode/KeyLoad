
namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PhysicalShardCatalogFenceTests
{
    [Test]
    public async Task AcScat003ReadFenceAdmitsOnlyTheExactConfiguredHostIdentity()
    {
        using var fixture = new PhysicalShardCatalogFixture();
        var shardId = Guid.Parse("10213243-5465-7687-98a9-bacbdcedfe0f");
        var incarnation = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var voters = PhysicalShardCatalogVoterIds.Standard;
        var request = PhysicalShardCatalogBootstrapTests.Request(shardId, incarnation, voters);
        await Assert.That(fixture.Bootstrap(request).Error).IsNull();
        var exact = fixture.Database.ReadPhysicalShardCatalog(PhysicalShardCatalogFixture.RootPrincipalId,
            shardId, incarnation, voters);
        await Assert.That(exact.DefaultShard.PhysicalShardId).IsEqualTo(shardId);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            fixture.Database.ReadPhysicalShardCatalog(PhysicalShardCatalogFixture.RootPrincipalId,
                Guid.NewGuid(), incarnation, voters));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.OwnershipLost);
    }
}
