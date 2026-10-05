using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class PartitionQueryPublicAuthorizationTests
{
    private const string DeniedTenant = "zz-denied";
    private const string RestrictedPath = "/secret";

    [Test]
    public async Task LaterPartitionDenialReturnsNoPartialPageAndDoesNotMutateStore()
    {
        using var fixture = new PartitionQueryPublicTestSupport();
        var denied = fixture.Second with { TenantId = DeniedTenant };
        fixture.ConfigureForeign(denied);
        fixture.AddRows(fixture.First, new PartitionQuerySeed("allowed", 2, "allowed"));
        fixture.AddRows(denied, new PartitionQuerySeed("denied", 1, "denied"));
        fixture.AddReader(fixture.First);
        var position = fixture.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => new QueryEngine(fixture.Database)
            .QueryPartitions(PartitionQueryPublicTestSupport.ReaderId,
                fixture.Request([fixture.First, denied]), fixture.ExpectedOwner));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(fixture.Position).IsEqualTo(position);
        var healthy = new QueryEngine(fixture.Database).QueryPartitions(
            PartitionQueryPublicTestSupport.ReaderId, fixture.Request([fixture.First]), fixture.ExpectedOwner);
        await Assert.That(healthy.Rows.Select(row => row.Reference.Id).SequenceEqual(["allowed"])).IsTrue();
    }

    [Test]
    public async Task ExpectedOwnerMismatchFailsClosedAndProtectedFieldUseStillApplies()
    {
        using var ownerFixture = new PartitionQueryPublicTestSupport();
        ownerFixture.AddRows(ownerFixture.First, new PartitionQuerySeed("one", 1, "visible"));
        var wrongOwner = ownerFixture.ExpectedOwner with { PhysicalShardId = Guid.NewGuid() };
        var ownerFailure = Assert.ThrowsExactly<KeyLoadException>(() => new QueryEngine(ownerFixture.Database)
            .QueryPartitions("root", ownerFixture.Request([ownerFixture.First]), wrongOwner));
        await Assert.That(ownerFailure.Code).IsEqualTo(ErrorCode.OwnershipLost);

        using var protectedFixture = new PartitionQueryPublicTestSupport(fields:
            [new(RestrictedPath, "sensitive")]);
        protectedFixture.AddRows(protectedFixture.First, new PartitionQuerySeed("secret-row", 1, "visible", "private"));
        protectedFixture.AddReader(protectedFixture.First);
        var query = protectedFixture.Query(1) with { Order = [new(RestrictedPath, false)] };
        var request = protectedFixture.Request([protectedFixture.First], 1, query);
        var fieldFailure = Assert.ThrowsExactly<KeyLoadException>(() => new QueryEngine(protectedFixture.Database)
            .QueryPartitions(PartitionQueryPublicTestSupport.ReaderId, request,
                protectedFixture.ExpectedOwner));
        await Assert.That(fieldFailure.Code).IsEqualTo(ErrorCode.PermissionDenied);
    }
}
