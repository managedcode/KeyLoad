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
        fixture.AddRows(fixture.First, new PartitionQueryPublicSeed("allowed", 2, "allowed"));
        fixture.AddRows(denied, new PartitionQueryPublicSeed("denied", 1, "denied"));
        fixture.AddReader(fixture.First);
        var position = fixture.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => new QueryEngine(fixture.Database, UnitExecutionOptions.QueryExecution())
            .QueryPartitions(PartitionQueryPublicTestSupport.ReaderId,
                PartitionQueryPublicTestSupport.Request([fixture.First, denied]), PartitionQueryPublicTestSupport.ExpectedOwner));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(fixture.Position).IsEqualTo(position);
        var healthy = new QueryEngine(fixture.Database, UnitExecutionOptions.QueryExecution()).QueryPartitions(
            PartitionQueryPublicTestSupport.ReaderId, PartitionQueryPublicTestSupport.Request([fixture.First]), PartitionQueryPublicTestSupport.ExpectedOwner);
        await Assert.That(healthy.Rows.Select(row => row.Reference.Id).SequenceEqual(["allowed"])).IsTrue();
    }

    [Test]
    public async Task ExpectedOwnerMismatchFailsClosedAndProtectedFieldUseStillApplies()
    {
        using var ownerFixture = new PartitionQueryPublicTestSupport();
        ownerFixture.AddRows(ownerFixture.First, new PartitionQueryPublicSeed("one", 1, "visible"));
        var ownerPosition = ownerFixture.Position;
        var wrongOwner = PartitionQueryPublicTestSupport.ExpectedOwner with { PhysicalShardId = Guid.NewGuid() };
        var ownerFailure = Assert.ThrowsExactly<KeyLoadException>(() => new QueryEngine(ownerFixture.Database, UnitExecutionOptions.QueryExecution())
            .QueryPartitions("root", PartitionQueryPublicTestSupport.Request([ownerFixture.First]), wrongOwner));
        await Assert.That(ownerFailure.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(ownerFixture.Position).IsEqualTo(ownerPosition);
        var ownerRetry = new QueryEngine(ownerFixture.Database, UnitExecutionOptions.QueryExecution()).QueryPartitions(
            "root", PartitionQueryPublicTestSupport.Request([ownerFixture.First]), PartitionQueryPublicTestSupport.ExpectedOwner);
        await PartitionQueryWholeFlowAssertions.AssertPublicRowAsync(ownerRetry, ownerFixture.First, "one", "visible");
        await Assert.That(ownerFixture.Position).IsEqualTo(ownerPosition);

        using var protectedFixture = new PartitionQueryPublicTestSupport(fields:
            [new(RestrictedPath, "sensitive")]);
        protectedFixture.AddRows(protectedFixture.First, new PartitionQueryPublicSeed("secret-row", 1, "visible", "private"));
        protectedFixture.AddReader(protectedFixture.First);
        var query = PartitionQueryPublicTestSupport.Query(1) with { Order = [new(RestrictedPath, false)] };
        var request = PartitionQueryPublicTestSupport.Request([protectedFixture.First], 1, query);
        var fieldPosition = protectedFixture.Position;
        var fieldFailure = Assert.ThrowsExactly<KeyLoadException>(() => new QueryEngine(protectedFixture.Database, UnitExecutionOptions.QueryExecution())
            .QueryPartitions(PartitionQueryPublicTestSupport.ReaderId, request,
                PartitionQueryPublicTestSupport.ExpectedOwner));
        await Assert.That(fieldFailure.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(protectedFixture.Position).IsEqualTo(fieldPosition);
        var healthyQuery = PartitionQueryPublicTestSupport.Request([protectedFixture.First], 1);
        var healthy = new QueryEngine(protectedFixture.Database, UnitExecutionOptions.QueryExecution())
            .QueryPartitions(PartitionQueryPublicTestSupport.ReaderId, healthyQuery, PartitionQueryPublicTestSupport.ExpectedOwner);
        await PartitionQueryWholeFlowAssertions.AssertPublicRowAsync(healthy, protectedFixture.First, "secret-row", "visible");
        await Assert.That(healthy.Rows[0].Row.Redacted).IsTrue();
        await Assert.That(healthy.Rows[0].Row.RedactedFields!.Value.SequenceEqual([RestrictedPath])).IsTrue();
        await Assert.That(protectedFixture.Position).IsEqualTo(fieldPosition);
    }
}
