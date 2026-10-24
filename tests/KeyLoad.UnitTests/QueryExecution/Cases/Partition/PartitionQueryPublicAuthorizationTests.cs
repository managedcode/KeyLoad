using KeyLoad.Query;

namespace KeyLoad.UnitTests.QueryExecution;

internal sealed class PartitionQueryPublicAuthorizationTests
{
    private const string DeniedTenant = "zz-denied";
    private const string RestrictedPath = "/secret";
    private const string RestrictedAlias = "secret";
    private const string RestrictedProjectionJson = "{\"secret\":null}";

    [Test]
    public async Task LaterPartitionDenialReturnsNoPartialPageAndDoesNotMutateStore()
    {
        using var fixture = new PartitionQueryPublicTestSupport();
        var denied = fixture.Second with
        {
            TenantId = DeniedTenant
        };
        fixture.ConfigureForeign(denied);
        fixture.AddRows(fixture.First, new PartitionQueryPublicSeed("allowed", 2, "allowed"));
        fixture.AddRows(denied, new PartitionQueryPublicSeed("denied", 1, "denied"));
        fixture.AddReader(fixture.First);
        var position = fixture.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => new QueryEngine(fixture.Database, UnitExecutionOptions.QueryExecution())
            .QueryPartitions(PartitionQueryPublicTestSupport.ReaderId,
                PartitionQueryPublicTestSupport.Request([fixture.First, denied]), fixture.ExpectedOwner));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(fixture.Position).IsEqualTo(position);
        var healthy = new QueryEngine(fixture.Database, UnitExecutionOptions.QueryExecution()).QueryPartitions(
            PartitionQueryPublicTestSupport.ReaderId, PartitionQueryPublicTestSupport.Request([fixture.First]), fixture.ExpectedOwner);
        await Assert.That(healthy.Rows.Select(row => row.Reference.Id).SequenceEqual(["allowed"])).IsTrue();
    }

    [Test]
    public async Task ExpectedOwnerMismatchFailsClosedAndProtectedFieldUseStillApplies()
    {
        using var ownerFixture = new PartitionQueryPublicTestSupport();
        ownerFixture.AddRows(ownerFixture.First, new PartitionQueryPublicSeed("one", 1, "visible"));
        var ownerPosition = ownerFixture.Position;
        var wrongOwner = ownerFixture.ExpectedOwner with
        {
            PhysicalShardId = Guid.NewGuid()
        };
        var ownerFailure = Assert.ThrowsExactly<KeyLoadException>(() => new QueryEngine(ownerFixture.Database, UnitExecutionOptions.QueryExecution())
            .QueryPartitions("root", PartitionQueryPublicTestSupport.Request([ownerFixture.First]), wrongOwner));
        await Assert.That(ownerFailure.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(ownerFixture.Position).IsEqualTo(ownerPosition);
        var ownerRetry = new QueryEngine(ownerFixture.Database, UnitExecutionOptions.QueryExecution()).QueryPartitions(
            "root", PartitionQueryPublicTestSupport.Request([ownerFixture.First]), ownerFixture.ExpectedOwner);
        await PartitionQueryWholeFlowAssertions.AssertPublicRowAsync(ownerRetry, ownerFixture.First, "one", "visible", ownerFixture.Creation);
        await Assert.That(ownerFixture.Position).IsEqualTo(ownerPosition);

        using var protectedFixture = new PartitionQueryPublicTestSupport(fields:
            [new(RestrictedPath, "sensitive")]);
        protectedFixture.AddRows(protectedFixture.First, new PartitionQueryPublicSeed("secret-row", 1, "visible", "private"));
        protectedFixture.AddReader(protectedFixture.First);
        var query = PartitionQueryPublicTestSupport.Query(1) with
        {
            Order = [new(RestrictedPath, false)]
        };
        var request = PartitionQueryPublicTestSupport.Request([protectedFixture.First], 1, query);
        var fieldPosition = protectedFixture.Position;
        var fieldFailure = Assert.ThrowsExactly<KeyLoadException>(() => new QueryEngine(protectedFixture.Database, UnitExecutionOptions.QueryExecution())
            .QueryPartitions(PartitionQueryPublicTestSupport.ReaderId, request,
                protectedFixture.ExpectedOwner));
        await Assert.That(fieldFailure.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(protectedFixture.Position).IsEqualTo(fieldPosition);
        var healthyQuery = PartitionQueryPublicTestSupport.Request([protectedFixture.First], 1);
        var healthy = new QueryEngine(protectedFixture.Database, UnitExecutionOptions.QueryExecution())
            .QueryPartitions(PartitionQueryPublicTestSupport.ReaderId, healthyQuery, protectedFixture.ExpectedOwner);
        await PartitionQueryWholeFlowAssertions.AssertPublicRowAsync(healthy, protectedFixture.First, "secret-row", "visible", protectedFixture.Creation);
        await Assert.That(healthy.Rows[0].Row.Redacted).IsFalse();
        await Assert.That(healthy.Rows[0].Row.RedactedFields!.Value).IsEmpty();
        var selectedQuery = healthyQuery with
        {
            Query = healthyQuery.Query with
            {
                Projection = [new(RestrictedPath, RestrictedAlias)]
            }
        };
        var selected = new QueryEngine(protectedFixture.Database, UnitExecutionOptions.QueryExecution())
            .QueryPartitions(PartitionQueryPublicTestSupport.ReaderId, selectedQuery, protectedFixture.ExpectedOwner);
        var selectedRow = selected.Rows.Single();
        await Assert.That(selected.Complete).IsTrue();
        await Assert.That(selectedRow.Reference).IsEqualTo(healthy.Rows[0].Reference);
        await Assert.That(selectedRow.Row.Revision).IsEqualTo(healthy.Rows[0].Row.Revision);
        await Assert.That(selectedRow.Row.Json).IsEqualTo(RestrictedProjectionJson);
        await Assert.That(selectedRow.Row.Redacted).IsTrue();
        await Assert.That(selectedRow.Row.RedactedFields!.Value.SequenceEqual([RestrictedPath])).IsTrue();
        await Assert.That(selected.Leaves.Single()).IsEqualTo(healthy.Leaves.Single());
        await Assert.That(protectedFixture.Position).IsEqualTo(fieldPosition);
    }
}
