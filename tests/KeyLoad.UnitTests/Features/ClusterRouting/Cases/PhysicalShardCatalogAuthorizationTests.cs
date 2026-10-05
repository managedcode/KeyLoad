
namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PhysicalShardCatalogAuthorizationTests
{
    [Test]
    public async Task AcScat001BootstrapAndReadRequirePersistedClusterAdministrator()
    {
        using var fixture = new PhysicalShardCatalogFixture();
        var reader = new PrincipalRecord("reader", "system", [], []);
        fixture.AddPrincipal(reader);
        var request = PhysicalShardCatalogBootstrapTests.Request(Guid.NewGuid(), Guid.NewGuid(),
            PhysicalShardCatalogVoterIds.Standard);
        var unauthorized = fixture.Database.CreateNativeOperation(OperationKind.BootstrapPhysicalShardCatalog,
            Guid.NewGuid(), reader.Id, fixture.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        await Assert.That(fixture.Database.Apply(unauthorized).Error).IsEqualTo(ErrorCode.PermissionDenied);
        var absent = Assert.ThrowsExactly<KeyLoadException>(() =>
            fixture.Database.ReadPhysicalShardCatalog(PhysicalShardCatalogFixture.RootPrincipalId));
        await Assert.That(absent.Code).IsEqualTo(ErrorCode.NotFound);
        var read = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Database.ReadPhysicalShardCatalog(reader.Id));
        await Assert.That(read.Code).IsEqualTo(ErrorCode.PermissionDenied);
    }
}
